using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ClaudeLab
{
    /// <summary>
    /// Base class for anything Claude can do in the lab.
    /// Add a subclass to a GameObject and drag it into ClaudeAgent's tool list.
    /// </summary>
    public abstract class LabTool : MonoBehaviour
    {
        /// <summary>Tool name Claude calls, e.g. "spawn_object". Letters, digits, underscores.</summary>
        public abstract string ToolName { get; }

        /// <summary>Plain-language description. Claude reads this to decide when to use the tool.</summary>
        public abstract string Description { get; }

        /// <summary>JSON Schema for the tool's input.</summary>
        public abstract JObject InputSchema { get; }

        /// <summary>
        /// Run the tool on Unity's main thread. Return a short text result for Claude.
        /// Throw (or return ToolResult.Error) if the input is bad; Claude sees the message and can retry.
        /// </summary>
        public abstract ToolResult Execute(JObject input);

        public JObject Definition => new JObject
        {
            ["name"] = ToolName,
            ["description"] = Description,
            ["input_schema"] = InputSchema,
        };
    }

    public readonly struct ToolResult
    {
        public readonly string Content;
        public readonly bool IsError;

        ToolResult(string content, bool isError)
        {
            Content = content;
            IsError = isError;
        }

        public static ToolResult Ok(string content) => new ToolResult(content, false);
        public static ToolResult Error(string message) => new ToolResult(message, true);
    }
}
