﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.ErrorReporting;
using System.Diagnostics;
using Microsoft.CodeAnalysis.Scripting.Hosting;

namespace RoslynPad.Roslyn.Completion.Providers;

internal abstract class AbstractDirectivePathCompletionProvider : CompletionProvider
{
    private static readonly char[] s_separators = PathUtilities.IsUnixLikePlatform
        ? ['/', ',']
        : ['/', ',', '\\'];

    protected abstract bool TryGetStringLiteralToken(SyntaxTree tree, int position, out SyntaxToken stringLiteral, CancellationToken cancellationToken);

    public sealed override async Task ProvideCompletionsAsync(CompletionContext context)
    {
        try
        {
            var document = context.Document;
            var position = context.Position;
            var cancellationToken = context.CancellationToken;

            var tree = await document.GetSyntaxTreeAsync(cancellationToken).ConfigureAwait(false);

            if (!TryGetStringLiteralToken(tree!, position, out var stringLiteral, cancellationToken))
            {
                return;
            }

            var literalValue = stringLiteral.ToString();

            context.CompletionListSpan = GetTextChangeSpan(
                quotedPath: literalValue,
                quotedPathStart: stringLiteral.SpanStart,
                position: position);

            var pathThroughLastSlash = GetPathThroughLastSlash(
                quotedPath: literalValue,
                quotedPathStart: stringLiteral.SpanStart,
                position: position);

            await ProvideCompletionsAsync(context, pathThroughLastSlash).ConfigureAwait(false);
        }
        catch (Exception e) when (FatalError.ReportAndPropagateUnlessCanceled(e))
        {
            // nop
        }
    }

    public override bool ShouldTriggerCompletion(SourceText text, int caretPosition, CompletionTrigger trigger, OptionSet options)
    {
        return true;
    }

    private static string GetPathThroughLastSlash(string quotedPath, int quotedPathStart, int position)
    {
        Contract.ThrowIfTrue(quotedPath[0] != '"');

        const int QuoteLength = 1;

        var positionInQuotedPath = position - quotedPathStart;
        var path = quotedPath.Substring(QuoteLength, positionInQuotedPath - QuoteLength).Trim();
        var afterLastSlashIndex = AfterLastSlashIndex(path, path.Length);

        // We want the portion up to, and including the last slash if there is one.  That way if
        // the user pops up completion in the middle of a path (i.e. "C:\Win") then we'll
        // consider the path to be "C:\" and we will show appropriate completions.
        return afterLastSlashIndex >= 0 ? path.Substring(0, afterLastSlashIndex) : path;
    }

    private static TextSpan GetTextChangeSpan(string quotedPath, int quotedPathStart, int position)
    {
        // We want the text change to be from after the last slash to the end of the quoted
        // path. If there is no last slash, then we want it from right after the start quote
        // character.
        var positionInQuotedPath = position - quotedPathStart;

        // Where we want to start tracking is right after the slash (if we have one), or else
        // right after the string starts.
        var afterLastSlashIndex = AfterLastSlashIndex(quotedPath, positionInQuotedPath);
        var afterFirstQuote = 1;

        var startIndex = Math.Max(afterLastSlashIndex, afterFirstQuote);
        var endIndex = quotedPath.Length;

        // If the string ends with a quote, the we do not want to consume that.
        if (EndsWithQuote(quotedPath))
        {
            endIndex--;
        }

        return TextSpan.FromBounds(startIndex + quotedPathStart, endIndex + quotedPathStart);
    }

    private static bool EndsWithQuote(string quotedPath)
    {
        return quotedPath.Length >= 2 && quotedPath[quotedPath.Length - 1] == '"';
    }

    /// <summary>
    /// Returns the index right after the last slash that precedes 'position'.  If there is no
    /// slash in the string, -1 is returned.
    /// </summary>
    private static int AfterLastSlashIndex(string text, int position)
    {
        // Position might be out of bounds of the string (if the string is unterminated.  Make
        // sure it's within bounds.
        position = Math.Min(position, text.Length - 1);

        int index;
        if ((index = text.LastIndexOfAny(s_separators, position)) >= 0)
        {
            return index + 1;
        }

        return -1;
    }

    protected abstract Task ProvideCompletionsAsync(CompletionContext context, string pathThroughLastSlash);

    protected static FileSystemCompletionHelper GetFileSystemCompletionHelper(
        Document document,
        Microsoft.CodeAnalysis.Glyph itemGlyph,
        ImmutableArray<string> extensions,
        CompletionItemRules completionRules)
    {
        ImmutableArray<string> referenceSearchPaths;
        string? baseDirectory;
        if (document.Project.CompilationOptions?.MetadataReferenceResolver is RuntimeMetadataReferenceResolver resolver)
        {
            referenceSearchPaths = resolver.PathResolver.SearchPaths;
            baseDirectory = resolver.PathResolver.BaseDirectory;
        }
        else
        {
            referenceSearchPaths = [];
            baseDirectory = null;
        }

        return new FileSystemCompletionHelper(
            Microsoft.CodeAnalysis.Glyph.OpenFolder,
            itemGlyph,
            referenceSearchPaths,
            GetBaseDirectory(document, baseDirectory),
            extensions,
            completionRules);
    }

    private static string GetBaseDirectory(Document document, string? baseDirectory)
    {
        var result = PathUtilities.GetDirectoryName(document.FilePath);
        if (!PathUtilities.IsAbsolute(result))
        {
            result = baseDirectory;
            Debug.Assert(result == null || PathUtilities.IsAbsolute(result));
        }

        return result ?? string.Empty;
    }
}
