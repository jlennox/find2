using System.Collections.Generic;
using NUnit.Framework;

namespace find2.Tests
{
    public class Tests
    {
        [Test]
        public void TestSingleItemAtRootIsFound()
        {
            const string foundItem = "I'm found";

            using var test = new FindTest(new[] {
                FindTestPath.File(foundItem),
                FindTestPath.File("I'm not found"),
            });

            var found = new List<string>();

            var find = new Find(foundItem, new[] { test.Root });
            find.Match += match => found.Add(match);
            find.Run();

            CollectionAssert.AreEquivalent(
                new[] { test.Combine(foundItem) },
                found);
        }

        [Test]
        public void TestMultipleItemsWithSomeDepth()
        {
            const string foundItem = "I'm found";

            using var test = new FindTest(new[] {
                FindTestPath.File("sub dir1", foundItem),
                FindTestPath.File("sub dir1", "also filename"),
                FindTestPath.File("sub dir2", foundItem),
                FindTestPath.File(foundItem),
            });

            var found = new List<string>();

            var find = new Find(foundItem, new[] { test.Root });
            find.Match += match => found.Add(match);
            find.Run();

            CollectionAssert.AreEquivalent(
                new[] {
                    test.Combine("sub dir1", foundItem),
                    test.Combine("sub dir2", foundItem),
                    test.Combine(foundItem),
                },
                found);
        }
    }
}