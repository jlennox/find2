using System;
using NUnit.Framework;

namespace find2.Tests;

internal class FindFileSizeTests
{
    [Test]
    [TestCase("23M", 1024 * 1024 * 23, FileSizeComparisonType.Equals, 1024 * 1024)]
    [TestCase("-23M", 1024 * 1024 * 23, FileSizeComparisonType.Less, 1024 * 1024)]
    [TestCase("+23M", 1024 * 1024 * 23, FileSizeComparisonType.Greater, 1024 * 1024)]
    [TestCase("23", 512 * 23, FileSizeComparisonType.Equals, 512)]
    [TestCase("23c", 1 * 23, FileSizeComparisonType.Equals, 1)]
    public void CheckValidPrefixesAndSuffixes(
        string input,
        long expectedSize,
        FileSizeComparisonType expectedType,
        long expectedUnit)
    {
        var findFileSize = new FindFileSize(input);
        Assert.AreEqual(expectedSize, findFileSize.Size);
        Assert.AreEqual(expectedType, findFileSize.Type);
        Assert.AreEqual(expectedUnit, findFileSize.Unit);
    }

    [Test]
    [TestCase("invalid")]
    [TestCase("-invalid")]
    [TestCase("+invalid")]
    [TestCase("+invalidM")]
    [TestCase("zM")]
    [TestCase("1z")]
    [TestCase("=1M")]
    [TestCase("")]
    public void InvalidInputExceptions(string input)
    {
        Assert.Throws<Exception>(() => new FindFileSize(input));
    }
}