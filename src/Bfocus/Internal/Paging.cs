using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Bfocus.Internal;

internal static class Paging
{
    internal const int DefaultPageSize = 100;

    internal static void CheckPageSize(int pageSize)
    {
        if (pageSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "pageSize precisa ser ≥ 1.");
        }
    }

    /// <summary>Percorre as páginas 1, 2, … até a última (ou até uma página vazia).</summary>
    internal static async IAsyncEnumerable<T> All<T>(Func<int, CancellationToken, Task<Page<T>>> fetchPage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var page = 1; ; page++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = await fetchPage(page, cancellationToken).ConfigureAwait(false);
            foreach (var item in current.Items)
            {
                yield return item;
            }

            if (current.Items.Count == 0 || page >= current.Pages)
            {
                yield break;
            }
        }
    }
}
