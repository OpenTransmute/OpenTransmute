namespace OpenTransmute.Models;

/// <summary>
/// Ecosystem classification for a detected project boundary marker (e.g. a <c>package.json</c> or
/// <c>Cargo.toml</c>). Used as a structural <em>hint</em> in the repo digest — it informs the LLM
/// clustering step but does not itself dictate segment boundaries.
/// </summary>
public enum SegmentKind
{
    /// <summary>No recognized marker / a plain directory.</summary>
    Unknown = 0,

    /// <summary>npm / pnpm / yarn package (<c>package.json</c>).</summary>
    Npm,

    /// <summary>.NET project (<c>*.csproj</c>, <c>*.fsproj</c>, <c>*.vbproj</c>).</summary>
    Dotnet,

    /// <summary>Rust crate (<c>Cargo.toml</c>).</summary>
    Cargo,

    /// <summary>Go module (<c>go.mod</c> / <c>go.work</c>).</summary>
    Go,

    /// <summary>Maven module (<c>pom.xml</c>).</summary>
    Maven,

    /// <summary>Gradle module (<c>build.gradle</c> / <c>settings.gradle</c>, Groovy or Kotlin DSL).</summary>
    Gradle,

    /// <summary>Python package (<c>pyproject.toml</c>, <c>setup.py</c>, <c>setup.cfg</c>).</summary>
    Python,

    /// <summary>PHP package (<c>composer.json</c>).</summary>
    Php,

    /// <summary>Ruby gem (<c>*.gemspec</c> / <c>Gemfile</c>).</summary>
    Ruby
}
