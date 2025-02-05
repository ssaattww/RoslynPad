﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace RoslynPad.Roslyn.BraceMatching;

internal abstract class AbstractDirectiveTriviaBraceMatcher<TDirectiveTriviaSyntax,
    TIfDirectiveTriviaSyntax, TElseIfDirectiveTriviaSyntax,
    TElseDirectiveTriviaSyntax, TEndIfDirectiveTriviaSyntax> : IBraceMatcher
        where TDirectiveTriviaSyntax : SyntaxNode
        where TIfDirectiveTriviaSyntax : TDirectiveTriviaSyntax
        where TElseIfDirectiveTriviaSyntax : TDirectiveTriviaSyntax
        where TElseDirectiveTriviaSyntax : TDirectiveTriviaSyntax
        where TEndIfDirectiveTriviaSyntax : TDirectiveTriviaSyntax
{
    internal abstract List<TDirectiveTriviaSyntax> GetMatchingConditionalDirectives(TDirectiveTriviaSyntax directive, CancellationToken cancellationToken);
    internal abstract TDirectiveTriviaSyntax? GetMatchingDirective(TDirectiveTriviaSyntax directive, CancellationToken cancellationToken);
    internal abstract TextSpan GetSpanForTagging(TDirectiveTriviaSyntax directive);

    public async Task<BraceMatchingResult?> FindBracesAsync(Document document, int position, CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        var token = root!.FindToken(position, findInsideTrivia: true);

        if (token.Parent is not TDirectiveTriviaSyntax directive)
        {
            return null;
        }

        TDirectiveTriviaSyntax? matchingDirective = null;
        if (IsConditionalDirective(directive))
        {
            // #if/#elif/#else/#endif directive cases.
            var matchingDirectives = GetMatchingConditionalDirectives(directive, cancellationToken);
            if (matchingDirectives?.Count > 0)
            {
                matchingDirective = matchingDirectives[(matchingDirectives.IndexOf(directive) + 1) % matchingDirectives.Count];
            }
        }
        else
        {
            // #region/#endregion or other directive cases.
            matchingDirective = GetMatchingDirective(directive, cancellationToken);
        }

        if (matchingDirective == null)
        {
            // one line directives, that do not have a matching begin/end directive pair.
            return null;
        }

        return new BraceMatchingResult(
            leftSpan: GetSpanForTagging(directive),
            rightSpan: GetSpanForTagging(matchingDirective));
    }


    private bool IsConditionalDirective(TDirectiveTriviaSyntax directive)
    {
        return directive is TIfDirectiveTriviaSyntax ||
               directive is TElseIfDirectiveTriviaSyntax ||
               directive is TElseDirectiveTriviaSyntax ||
               directive is TEndIfDirectiveTriviaSyntax;
    }
}
