using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTransmute.Llm;
using OpenTransmute.Models;

namespace OpenTransmute.Exploration;

/// <summary>
/// The LLM clustering step ("evaluator") of the mono-repo segmentation pipeline. Takes a deterministic
/// <see cref="RepoDigest"/> and a configured <see cref="ILlmExecutor"/>, asks the model to carve the
/// repository into a handful of logical, independently-decomposable segments, and resolves the model's
/// chosen paths back against the digest to produce a sized <see cref="RepoMap"/>.
/// </summary>
/// <remarks>
/// The model never sees the filesystem — only the digest text. The whole point of the digest's
/// homogeneity collapse is that a folder of 250 near-identical tools arrives as one summarized group,
/// so the model groups by role instead of emitting 250 segments. Driving the shared
/// <see cref="ILlmExecutor"/> (rather than a raw chat client) means the call honors whatever backend
/// the user configured — the Copilot CLI subprocess, OpenAI, Ollama, or Claude — with that backend's
/// own auth, exactly like a decompose job.
/// </remarks>
public sealed class RepoClusterer
{
    #region Members

    private readonly ILogger<RepoClusterer> _logger;

    // Model-authored JSON — tolerate casing drift on the property names.
    private static readonly JsonSerializerOptions _readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Constructor

    public RepoClusterer(ILogger<RepoClusterer> logger)
    {
        _logger = logger;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Asks the model to cluster <paramref name="digest"/> into logical segments and returns the sized map.
    /// </summary>
    /// <param name="digest">The deterministic structural digest to cluster.</param>
    /// <param name="executor">The configured backend executor (Copilot CLI, OpenAI, Ollama, or Claude).</param>
    /// <param name="request">Per-call backend parameters (model, endpoint, key, timeout, context tier).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A populated <see cref="RepoMap"/>. Throws on backend failure or an unparseable response.</returns>
    public async Task<RepoMap> ClusterAsync(
        RepoDigest digest,
        ILlmExecutor executor,
        ClusterRequest request,
        CancellationToken ct = default)
    {
        if (digest is null) throw new ArgumentNullException(nameof(digest));
        if (executor is null) throw new ArgumentNullException(nameof(executor));
        if (request is null) throw new ArgumentNullException(nameof(request));

        string guidance = GroupCountGuidance(digest.TotalBytes);

        LlmExecutionContext ctx = new LlmExecutionContext
        {
            SystemPrompt = BuildSystemPrompt(guidance),
            UserPrompt = digest.Render(),
            Model = request.Model,
            Endpoint = request.Endpoint,
            ApiKey = request.ApiKey,
            Timeout = request.Timeout,
            ContextTier = request.ContextTier,
            MaxOutputTokens = 8000,
            PhaseLabel = "cluster",
            // Ask agentic backends (Copilot/Claude) for a single clean JSON object on stdout — no fences,
            // no preamble, no sub-agent orchestration.
            JsonOutputMode = true
        };

        _logger.LogInformation("ClusterAsync: digest {Files} files / {Bytes} bytes, backend {Backend}, guidance '{Guidance}'",
            digest.TotalFileCount, digest.TotalBytes, executor.BackendType, guidance);

        string? output = null;
        await foreach (LlmOutputEvent ev in executor.ExecuteAsync(ctx, ct))
        {
            switch (ev)
            {
                case LlmCompleted completed:
                    output = completed.Output;
                    break;
                case LlmFailed failed:
                    _logger.LogError("ClusterAsync: backend reported failure: {Error}", failed.Error);
                    throw new InvalidOperationException($"Clustering call failed: {failed.Error}");
                // LlmLine events are streaming deltas — the terminal LlmCompleted carries the full text.
            }
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new InvalidOperationException("Clustering call returned empty content.");

        string raw = output;
        List<SegmentDto> dtos = ParseSegments(raw);

        List<RepoSegment> segments = BuildDisjointSegments(dtos, digest, out List<string> overlaps);

        _logger.LogInformation(
            "ClusterAsync: model proposed {Count} segments; {Overlaps} overlap(s) resolved",
            segments.Count, overlaps.Count);

        return new RepoMap
        {
            RootPath = digest.RootPath,
            ProjectName = digest.ProjectName,
            GeneratedAtUtc = DateTime.UtcNow,
            TargetGuidance = guidance,
            Segments = segments,
            Overlaps = overlaps,
            RawResponse = raw
        };
    }

    /// <summary>Builds the system prompt, embedding the digest notation key and the target group-count band.</summary>
    private static string BuildSystemPrompt(string guidance) =>
        "You are a senior software architect partitioning a large mono-repository so that each partition " +
        "can be documented and decomposed independently of the others.\n\n" +
        "You are given a deterministic STRUCTURAL DIGEST (not the source). Notation:\n" +
        "- Each line is a directory: `name/ — N files, size [.ext count, ...]`.\n" +
        "- `{Dotnet}`, `{Npm}`, `{Python}`, etc. mark an ecosystem manifest (a .csproj / package.json / pyproject) in that directory.\n" +
        "- `…(subdirs not expanded)` means deeper structure exists but was omitted to keep the digest small.\n" +
        "- `\u25a3 N similar subdirs [profile]` is a COLLAPSED set of near-identical sibling directories, listed by name underneath. Treat such a set as a single candidate grouping.\n\n" +
        "Goal: group directories into logical segments by SUBSYSTEM or ROLE \u2014 not one-to-one with folders.\n" +
        "Rules:\n" +
        "1. Merge many small related directories into one coherent segment.\n" +
        "2. Keep genuinely large, distinct subsystems as their own segments.\n" +
        "3. Put vendored / third-party / imported trees (e.g. ICU, FaceFX, bison, jinja2, psutil) into one or more `vendored` segments, separate from first-party code.\n" +
        "4. PARTITION, do not overlap: every path appears in EXACTLY ONE segment. Never list the same path twice, " +
        "and never list a directory in one segment while listing a subdirectory of it in another. If only part of a " +
        "directory belongs elsewhere, list that specific subdirectory once and leave the parent out.\n" +
        "5. Classify by what the code DOES, not by where it sits. A model/texture/shader exporter is asset-pipeline " +
        "tooling; an anti-cheat or security tool is its own concern; build/automation scripts are build-tooling; " +
        "tests are tests. Do not lump unrelated tools together just because they share a parent folder.\n" +
        "6. Do not invent paths \u2014 use only paths shown in the digest.\n" +
        $"7. Aim for {guidance}. Prefer fewer, well-reasoned segments over many tiny ones.\n\n" +
        "Respond with STRICT JSON ONLY — no prose, no markdown fences. A single JSON object with one key " +
        "\"segments\", whose value is an array where each element is:\n" +
        "{\"name\": string, \"role\": string, \"paths\": [string, ...], \"rationale\": string}\n" +
        "`role` is a short kebab-case label (e.g. \"build-tooling\", \"localization\", \"vendored\", \"web-frontend\").";

    /// <summary>
    /// Extracts the segment list from the model response. Prefers the object form
    /// <c>{"segments":[...]}</c> requested by the prompt, but falls back to a bare JSON array.
    /// Tolerates leading/trailing prose or markdown fences by slicing to the outermost braces/brackets.
    /// </summary>
    private List<SegmentDto> ParseSegments(string raw)
    {
        // Preferred shape: a single object with a "segments" array.
        int objStart = raw.IndexOf('{');
        int objEnd = raw.LastIndexOf('}');
        if (objStart >= 0 && objEnd > objStart)
        {
            string objJson = raw.Substring(objStart, objEnd - objStart + 1);
            try
            {
                ClusterResponseDto? wrapper = JsonSerializer.Deserialize<ClusterResponseDto>(objJson, _readOptions);
                if (wrapper?.Segments is { Count: > 0 } segments)
                    return segments;
            }
            catch (JsonException)
            {
                // Fall through to the bare-array path — the model may have emitted a top-level array.
            }
        }

        // Fallback shape: a bare JSON array.
        int start = raw.IndexOf('[');
        int end = raw.LastIndexOf(']');
        if (start < 0 || end <= start)
        {
            _logger.LogError("ClusterAsync: no JSON segments found in response ({Len} chars)", raw.Length);
            throw new InvalidOperationException("Model response did not contain a JSON segments array.");
        }

        string json = raw.Substring(start, end - start + 1);
        try
        {
            List<SegmentDto>? parsed = JsonSerializer.Deserialize<List<SegmentDto>>(json, _readOptions);
            return parsed ?? new List<SegmentDto>();
        }
        catch (JsonException ex)
        {
            _logger.LogError("ClusterAsync: JSON parse failed: {Message}", ex.Message);
            throw new InvalidOperationException($"Could not parse clustering JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Turns the model's raw segments into a strict disjoint partition. Every claimed path is resolved to
    /// its canonical digest key, then each key is assigned to exactly one segment:
    /// <list type="bullet">
    /// <item>An exact path listed in two segments is kept by the first claimant; the later one is dropped.</item>
    /// <item>When one segment claims a directory that nests inside another segment's directory, the files
    /// are counted only under the most specific (deepest) claim, so no byte is counted twice.</item>
    /// </list>
    /// Both kinds of resolution are recorded in <paramref name="overlaps"/> so the caller can see exactly
    /// what the model got wrong and where the contested files landed.
    /// </summary>
    private List<RepoSegment> BuildDisjointSegments(
        List<SegmentDto> dtos, RepoDigest digest, out List<string> overlaps)
    {
        IReadOnlyDictionary<string, PathStat> stats = digest.PathStats;
        overlaps = new List<string>();

        int count = dtos.Count;
        string[] names = new string[count];                      // stable display name per segment
        List<string>[] displayPaths = new List<string>[count];   // paths to render per segment
        List<string>[] ownedKeys = new List<string>[count];      // canonical keys this segment owns
        Dictionary<string, int> owner = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Pass 1: resolve paths and assign each canonical key to its first claimant.
        for (int i = 0; i < count; i++)
        {
            names[i] = string.IsNullOrWhiteSpace(dtos[i].Name) ? "(unnamed)" : dtos[i].Name!.Trim();
            displayPaths[i] = new List<string>();
            ownedKeys[i] = new List<string>();

            IEnumerable<string> normalized = (dtos[i].Paths ?? new List<string>())
                .Select(NormalizePath)
                .Where(p => p.Length > 0);

            foreach (string path in normalized)
            {
                string? key = ResolveKey(path, stats, digest.ProjectName);
                if (key is null)
                {
                    // Data-only directory with no source files — keep it visible, but it weighs nothing.
                    displayPaths[i].Add(path);
                    continue;
                }

                if (owner.TryGetValue(key, out int firstSeg))
                {
                    overlaps.Add(
                        $"duplicate '{key}' listed in '{names[firstSeg]}' and '{names[i]}' \u2014 kept in '{names[firstSeg]}'");
                    continue;
                }

                owner[key] = i;
                ownedKeys[i].Add(key);
                displayPaths[i].Add(path);
            }
        }

        // Pass 2: disjoint sizing. Each owned key starts at its full subtree, then donates its full subtree
        // away from its nearest owned ancestor — so a parent never counts a child that some segment owns.
        List<string> allKeys = owner.Keys.ToList();
        Dictionary<string, (int Files, long Bytes)> exclusive =
            new Dictionary<string, (int, long)>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in allKeys)
        {
            PathStat s = stats[key];
            exclusive[key] = (s.FileCount, s.Bytes);
        }

        foreach (string child in allKeys)
        {
            string? parent = NearestOwnedAncestor(child, allKeys);
            if (parent is null) continue;

            PathStat cs = stats[child];
            (int Files, long Bytes) cur = exclusive[parent];
            exclusive[parent] = (cur.Files - cs.FileCount, cur.Bytes - cs.Bytes);

            if (owner[parent] != owner[child])
                overlaps.Add(
                    $"'{parent}' ('{names[owner[parent]]}') contains '{child}' ('{names[owner[child]]}') " +
                    $"\u2014 those files counted only in '{names[owner[child]]}'");
        }

        // Pass 3: materialize segments with their disjoint totals.
        List<RepoSegment> segments = new List<RepoSegment>(count);
        for (int i = 0; i < count; i++)
        {
            int files = 0;
            long bytes = 0;
            foreach (string key in ownedKeys[i])
            {
                (int Files, long Bytes) e = exclusive[key];
                files += e.Files;
                bytes += e.Bytes;
            }

            // Defensive clamp: a partition can't yield negative weight, but guard against stat drift.
            if (files < 0) files = 0;
            if (bytes < 0) bytes = 0;

            SegmentDto dto = dtos[i];
            segments.Add(new RepoSegment
            {
                Name = names[i],
                Role = dto.Role?.Trim() ?? string.Empty,
                Paths = displayPaths[i],
                Rationale = dto.Rationale?.Trim() ?? string.Empty,
                FileCount = files,
                Bytes = bytes
            });
        }

        return segments;
    }

    /// <summary>
    /// Returns the longest key in <paramref name="all"/> that is a strict directory ancestor of
    /// <paramref name="key"/> (i.e. <paramref name="key"/> starts with <c>ancestor + "/"</c>), or null
    /// when no claimed ancestor exists. Used to attribute nested files to the most specific claim.
    /// </summary>
    private static string? NearestOwnedAncestor(string key, List<string> all)
    {
        string? best = null;
        foreach (string candidate in all)
        {
            if (candidate.Length >= key.Length) continue;
            if (!key.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase)) continue;
            if (best is null || candidate.Length > best.Length) best = candidate;
        }

        return best;
    }

    /// <summary>
    /// Maps a model-supplied path to its key in the stats map. The model copies the rendered
    /// "ProjectName/..." paths, but may occasionally drop or duplicate the root segment — so this tries
    /// the path verbatim, then prefixed with the project name, then with a leading project-name segment
    /// stripped. Returns null when nothing matches (e.g. a data-only directory with no source files).
    /// </summary>
    private static string? ResolveKey(
        string path, IReadOnlyDictionary<string, PathStat> stats, string projectName)
    {
        if (stats.ContainsKey(path)) return path;

        string prefixed = $"{projectName}/{path}";
        if (stats.ContainsKey(prefixed)) return prefixed;

        string prefix = projectName + "/";
        if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            string stripped = path.Substring(prefix.Length);
            if (stats.ContainsKey(stripped)) return stripped;
        }

        return null;
    }

    /// <summary>Normalizes a model-supplied path: backslashes to forward, trimmed, no leading/trailing slash.</summary>
    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Replace('\\', '/').Trim().Trim('/');
    }

    /// <summary>Size-banded guidance for how many segments to aim for, mirroring the decompose group bands.</summary>
    private static string GroupCountGuidance(long totalBytes)
    {
        const long mb = 1024 * 1024;
        return totalBytes switch
        {
            <= 200 * mb => "3-8 groups",
            <= 500 * mb => "5-10 groups",
            <= 800 * mb => "10-15 groups",
            <= 1200 * mb => "15-20 groups",
            _ => "20-30 groups"
        };
    }

    #endregion

    #region Nested types

    /// <summary>Wire shape for the model's top-level response object: <c>{"segments":[...]}</c>.</summary>
    private sealed class ClusterResponseDto
    {
        [JsonPropertyName("segments")] public List<SegmentDto>? Segments { get; set; }
    }

    /// <summary>Wire shape for a single segment in the model's JSON response.</summary>
    private sealed class SegmentDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("paths")] public List<string>? Paths { get; set; }
        [JsonPropertyName("rationale")] public string? Rationale { get; set; }
    }

    #endregion
}
