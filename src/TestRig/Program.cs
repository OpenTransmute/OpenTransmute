using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenTransmute.Orchestrator.Plugins;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        Console.WriteLine("TestRig — ListFiles LLM demo");
        Console.WriteLine();
        LLMTreeTest().Wait();
    }

    private static async Task LLMTreeTest()
    {

        // Configuration from environment variables
        string apiKey = "";
        string baseUrl = "";
        string model = "gpt-5.4-mini";

        // Directory to explore — first CLI arg or current directory
        string rootDir = "";
        Console.WriteLine($"Root: {rootDir}");
        Console.WriteLine($"Model: {model}");
        FileSystemPlugin plugin = new FileSystemPlugin(rootDir, File.ReadAllText(".transmuteignore"));
        Console.WriteLine("Extensions: \n" + string.Join("\n", plugin.ListExtensions()));

        // Build the chat client with automatic function invocation
        OpenAIClientOptions clientOptions = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient { Timeout = TimeSpan.FromMinutes(5) }),
            NetworkTimeout = TimeSpan.FromMinutes(5),
            RetryPolicy = new ClientRetryPolicy(maxRetries: 0),
        };

        if (baseUrl != "https://api.openai.com/v1")
            clientOptions.Endpoint = new Uri(baseUrl);

        OpenAIClient openAiClient = new OpenAIClient(new ApiKeyCredential(apiKey), clientOptions);

        IChatClient client = openAiClient.GetChatClient(model)
            .AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();

        // Register the ListFiles tool from FileSystemPlugin
        // Note: ListFiles resolves paths relative to the CWD, so set CWD to rootDir.
        Directory.SetCurrentDirectory(rootDir);

        IList<AITool> tools = [AIFunctionFactory.Create(plugin.ListFiles), AIFunctionFactory.Create(plugin.ListExtensions)];

        // Send the prompt
        List<ChatMessage> messages =
        [
            new ChatMessage(ChatRole.System,
                "You are a helpful assistant that explores file systems. " +
                "ListExtensions can look through the files and tell you what extensions there are and how many." +
                "When asked to list files, call the ListFiles tool and then format the result " +
                "as a clean indented tree. Use '├── ' for non-last entries and '└── ' for last entries. " +
                "Show the file size next to each file. Show directories in bold by wrapping with **name/**."),
            new ChatMessage(ChatRole.User,
                "You must discover ONLY software source code files in the current folder (use '.') " +
                "and display the result as a nicely formatted hierarchy tree.  Do not truncate output!" +
                "Supplemental: zsh files are plugin code."),
        ];

        ChatOptions options = new ChatOptions
        {
            Tools = [.. tools],
            ToolMode = ChatToolMode.Auto,
            MaxOutputTokens = 64000
        };

        Console.WriteLine("Querying LLM...");
        Console.WriteLine(new string('─', 60));
        Console.WriteLine();

        ChatResponse response = await client.GetResponseAsync(messages, options);
        Console.WriteLine(response.Text);
    }

    private static void PrintTree(IEnumerable<string> lines)
    {
        // Each line: <ID>|<DEPTH>|<TYPE>|<NAME>|<SIZE>
        var root     = new TreeNode { Name = "ROOT", Type = "d" };
        var depthMap = new Dictionary<int, TreeNode> { [-1] = root };

        foreach (string line in lines)
        {
            string[] p  = line.Split('|');
            int    depth = int.Parse(p[1]);
            string type  = p[2];
            string name  = p[3];
            string size  = p[4];

            var node = new TreeNode { Name = name, Type = type, Size = size };
            depthMap[depth - 1].Children.Add(node);
            depthMap[depth] = node;
        }

        //Walk(root.Children[0].Children, "");
        Walk(root.Children, "");

        static void Walk(List<TreeNode> children, string indent)
        {
            for (int i = 0; i < children.Count; i++)
            {
                TreeNode node   = children[i];
                bool     isLast = i == children.Count - 1;
                string   conn   = isLast ? "└── " : "├── ";
                string   label  = node.Type == "d" ? $"**{node.Name}/**" : $"{node.Name} ({node.Size})";

                Console.WriteLine(indent + conn + label);
                Walk(node.Children, indent + (isLast ? "    " : "│   "));
            }
        }
    }

    private sealed class TreeNode
    {
        public string       Name     { get; init; } = "";
        public string       Type     { get; init; } = "";
        public string       Size     { get; init; } = "";
        public List<TreeNode> Children { get; }     = [];
    }

}
