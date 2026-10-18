using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.AzureIllusion.ScheduledTasks;

internal static class LibraryPager
{
    public static IEnumerable<BaseItem> Read(ILibraryManager manager, InternalItemsQuery query, CancellationToken token)
    {
        const int pageSize = 400;
        query.Limit = pageSize;
        for (var offset = 0; ; offset += pageSize)
        {
            token.ThrowIfCancellationRequested();
            query.StartIndex = offset;
            var page = manager.GetItemList(query);
            foreach (var item in page) yield return item;
            if (page.Count < pageSize) yield break;
        }
    }
}
