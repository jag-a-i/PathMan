using System.Linq;
using PathManager.Core.Models;
using Xunit;

namespace PathManager.Tests;

public class PathListTests
{
    [Fact]
    public void PathList_ParsesEntriesAndPreservesTokens()
    {
        var raw = @"C:\Bin;%SystemRoot%\System32;C:\Tools;;C:\Bin";
        var list = new PathList(raw);

        Assert.Equal(4, list.Count);
        Assert.Equal(@"C:\Bin", list.Entries[0].Raw);
        Assert.Equal(@"%SystemRoot%\System32", list.Entries[1].Raw);
        Assert.Equal(@"C:\Tools", list.Entries[2].Raw);
        Assert.Equal(@"C:\Bin", list.Entries[3].Raw);

        var deduplicated = list.Deduplicate();
        Assert.Equal(3, deduplicated.Count);
    }

    [Fact]
    public void PathList_Deduplicate_PreservesOrderAndRemovesDuplicates()
    {
        var raw = @"C:\Bin;C:\Tools;c:\bin;C:\Tools\Sub;C:\TOOLS";
        var list = new PathList(raw);

        var deduplicated = list.Deduplicate();

        Assert.Equal(3, deduplicated.Count);
        Assert.Equal(@"C:\Bin", deduplicated.Entries[0].Raw);
        Assert.Equal(@"C:\Tools", deduplicated.Entries[1].Raw);
        Assert.Equal(@"C:\Tools\Sub", deduplicated.Entries[2].Raw);
    }

    [Fact]
    public void PathList_Prepend_InsertsAtBeginningAndMovesExisting()
    {
        var list = new PathList(@"C:\Tools;C:\Bin;C:\Shims");
        list.Prepend(@"C:\Shims");

        Assert.Equal(3, list.Count);
        Assert.Equal(@"C:\Shims", list.Entries[0].Raw);
        Assert.Equal(@"C:\Tools", list.Entries[1].Raw);
        Assert.Equal(@"C:\Bin", list.Entries[2].Raw);
    }

    [Fact]
    public void PathList_Remove_RemovesMatchingEntry()
    {
        var list = new PathList(@"C:\Tools;C:\Bin;C:\Shims");
        var changed = list.Remove(@"c:\bin");

        Assert.True(changed);
        Assert.Equal(2, list.Count);
        Assert.DoesNotContain(list.Entries, e => e.Raw == @"C:\Bin");
    }

    [Fact]
    public void PathList_Combine_PlacesMachineBeforeUser()
    {
        var machine = new PathList(@"C:\Windows\System32;C:\Windows");
        var user = new PathList(@"C:\Users\test\bin;C:\Tools");

        var combined = PathList.Combine(machine, user);

        Assert.Equal(4, combined.Count);
        Assert.Equal(@"C:\Windows\System32", combined.Entries[0].Raw);
        Assert.Equal(@"C:\Windows", combined.Entries[1].Raw);
        Assert.Equal(@"C:\Users\test\bin", combined.Entries[2].Raw);
        Assert.Equal(@"C:\Tools", combined.Entries[3].Raw);
    }
}
