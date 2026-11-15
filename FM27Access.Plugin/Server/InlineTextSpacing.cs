#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FM27Access.Plugin.Server;

// Recover separators from complete native text, rather than inventing spaces
// around every embedded link (which changes punctuation/unspaced languages).
internal static class InlineTextSpacing
{
    internal static bool TryRead(string? text,IReadOnlyList<string>? captions,[NotNullWhen(true)] out string[]? separators)
    {
        separators=null;
        if(text==null||captions==null||captions.Count==0)return false;
        var result=new string[captions.Count];int end=0;
        for(int i=0;i<captions.Count;i++)
        {
            var caption=captions[i];if(string.IsNullOrEmpty(caption))return false;
            int start=text.IndexOf(caption,end,StringComparison.Ordinal);
            if(start<0)return false;
            string separator=text.Substring(end,start-end);
            // Never silently lose a missing fragment by treating it as spacing.
            if(!string.IsNullOrWhiteSpace(separator)&&separator.Length!=0)return false;
            result[i]=separator;end=start+caption.Length;
        }
        if(!string.IsNullOrWhiteSpace(text.Substring(end)))return false;
        separators=result;return true;
    }
}
