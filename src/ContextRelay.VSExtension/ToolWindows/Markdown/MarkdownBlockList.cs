using System.Collections.Generic;
using Microsoft.VisualStudio.Extensibility.UI;

namespace ContextRelay.VSExtension.ToolWindows.Markdown;

/// <summary>Keeps an <see cref="ObservableList{T}"/> of blocks in sync with a new parse, updating in place.</summary>
internal static class MarkdownBlockList
{
    internal static void Update(ObservableList<MarkdownBlockViewModel> target, IReadOnlyList<MarkdownBlock> blocks)
    {
        var shared = System.Math.Min(target.Count, blocks.Count);
        for (var index = 0; index < shared; index++)
        {
            // A block whose kind changed (a paragraph that became a heading as more text arrived) is
            // still updated in place; its flags are re-raised by CopyFrom.
            target[index].CopyFrom(blocks[index]);
        }

        while (target.Count > blocks.Count)
        {
            target.RemoveAt(target.Count - 1);
        }

        for (var index = target.Count; index < blocks.Count; index++)
        {
            target.Add(new MarkdownBlockViewModel(blocks[index]));
        }
    }
}
