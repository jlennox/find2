using System;
using NUnit.Framework;

namespace find2.Tests;

public sealed class MemoryFileEntry : IFileEntry
{
    public bool IsDirectory { get; init; }
    public string Name { get; init; }
    public DateTime LastAccessTime { get; init; }
    public DateTime LastWriteTime { get; init; }
    public long Size { get; init; }
}

public class ExpressionMatchTests
{
    private static MemoryFileEntry File(string name, bool toUpper = false)
    {
        return new() { Name = toUpper ? name.ToUpper() : name };
    }

    private static void Test(string param)
    {
        Test(param, null, null);
    }

    private static void Test(string param, string[] matches, string[] mismatches, bool toUpper = false)
    {
        Test(param.Split(' '), matches, mismatches, toUpper);
    }

    private static void Test(string[] param, string[] matches, string[] mismatches, bool toUpper = false)
    {
        var matcher = ExpressionMatch.Build(param).Match;

        foreach (var match in matches ?? Array.Empty<string>())
        {
            Assert.IsTrue(matcher == null || matcher(File(match, toUpper)));
        }

        foreach (var mismatch in mismatches ?? Array.Empty<string>())
        {
            Assert.IsFalse(matcher != null && matcher(File(mismatch, toUpper)));
        }
    }

    // Ensure the `Test()` method works as expected.
    [Test]
    public void TestSanityCheck()
    {
        Assert.Throws<AssertionException>(() =>
            Test("-name foobaxr", new[] {
                "foobar"
            }, new[] {
                "notfoobar",
                "foobarnot"
            }));

        Assert.Throws<AssertionException>(() =>
            Test("-name foobaxr", new[] {
                "foobar"
            }, new[] {
                "foobaxr",
                "foobarnot"
            }));

        Test("-name FOOBAR", new[] {
            "foobar"
        }, new[] {
            "foobaxr",
            "foobarnot"
        }, true);

        Assert.Throws<AssertionException>(() =>
            Test("-name foobar", new[] {
                "foobar"
            }, new[] {
                "foobaxr",
                "foobarnot"
            }, true));
    }

    // Ensure nothing is using the wrong match method. It's possible for
    // most of the tests to pass as those it has the inner globs.
    [Test]
    [TestCase("foobar", nameof(ExpressionMatch.NameEquals))]
    [TestCase("foobar*", nameof(ExpressionMatch.NameStartsWith))]
    [TestCase("*foobar", nameof(ExpressionMatch.NameEndsWith))]
    [TestCase("*foobar*", nameof(ExpressionMatch.NameContains))]
    [TestCase("foo*bar", nameof(ExpressionMatch.NameRegex))]
    [TestCase("*foo*bar*", nameof(ExpressionMatch.NameRegex))]
    [TestCase("*foo*bar", nameof(ExpressionMatch.NameRegex))]
    [TestCase("foo*bar*", nameof(ExpressionMatch.NameRegex))]
    public void CorrectMatchMethod(string match, string expectedMethod)
    {
        ExpressionMatch.NameBlob(match, false, out var actualMethod);
        Assert.AreEqual(expectedMethod, actualMethod);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void NoGlob(string param, bool toUpper)
    {
        Test($"{param} foobar",
            matches: new[] {
                "foobar",
            },
            mismatches: new[] {
                "notfoobar",
                "foobarnot",
            }, toUpper);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void StartingGlob(string param, bool toUpper)
    {
        Test($"{param} *foobar",
            matches: new[] {
                "foobar",
                "notfoobar",
            },
            mismatches: new[] {
                "foobarnot"
            }, toUpper);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void EndingGlob(string param, bool toUpper)
    {
        Test($"{param} foobar*",
            matches: new[] {
                "foobar",
                "foobarnot",
            },
            mismatches: new[] {
                "notfoobar",
            }, toUpper);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void StartingAndEndingGlob(string param, bool toUpper)
    {
        Test($"{param} *foobar*",
            matches: new[] {
                "foobar",
                "foobarnot",
                "notfoobar",
            },
            mismatches: null, toUpper);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void CenterGlobs(string param, bool toUpper)
    {
        Test($"{param} *foo*bar*",
            matches: new[] {
                "foobar",
                "notfoobar",
                "foobarnot",
                "foonotbar",
            },
            mismatches: new[] {
                "foo",
                "bar",
            }, toUpper);
    }

    [Test]
    [TestCase("-name", false)]
    [TestCase("-iname", true)]
    public void MultipleExpressions(string param, bool toUpper)
    {
        Test($"{param} *foo*bar* {param} *foo*bar* {param} *foo*bar*",
            matches: new[] {
                "foobar",
                "notfoobar",
                "foobarnot",
                "foonotbar",
            },
            mismatches: new[] {
                "foo",
                "bar",
            }, toUpper);
    }

    [Test]
    [TestCase(false)]
    [TestCase(true)]
    public void EmptyAlwaysMatches(bool toUpper)
    {
        Test(Array.Empty<string>(),
            matches: new[] {
                "foobar",
                "notfoobar",
                "foobarnot",
                "foonotbar",
            },
            mismatches: null,
            toUpper);
    }

    [Test]
    [TestCase("!")]
    [TestCase("-not")]
    public void NotInvertsMatch(string param)
    {
        Test($"{param} -name foobar",
            matches: new[] {
                "notfoobar",
                "foobarnot",
            },
            mismatches: new[] {
                "foobar",
            });
    }

    [Test]
    [TestCase("-or")]
    [TestCase("-o")]
    public void OrOperator(string param)
    {
        Test($"-name foobar {param} -name foobar2",
            matches: new[] {
                "foobar",
                "foobar2",
            },
            mismatches: new[] {
                "notfoobar",
                "foobarnot",
            });
    }

    [Test]
    public void Parentheses()
    {
        Test("-name foobar* -and ( -name *.baz -or -name *.faz )",
            matches: new[] {
                "foobar.baz",
                "foobar.faz",
            },
            mismatches: new[] {
                "foobar",
                "notfoobar.bazz",
                "foobarnot",
            });

        // Starting/ending parentheses are simplified out because they'd need to be special cased if not.
        Test("( -name foobar* )",
            matches: new[] {
                "foobar.baz",
                "foobar.faz",
            },
            mismatches: null);

        // Empty parentheses don't blow up.
        Test("-true ( )",
            matches: new[] {
                "foobar.baz",
                "foobar.faz",
            },
            mismatches: null);

        Test("( )",
            matches: new[] {
                "foobar.baz",
                "foobar.faz",
            },
            mismatches: null);

        // Unbalanced parenthesis exception.
        Assert.Throws<Exception>(() => Test("-name foobar* ) -true"));
        Assert.Throws<Exception>(() => Test("-name foobar* ( -true"));
        Assert.Throws<Exception>(() => Test("( -name foobar* ( ( -true ) )"));
        Assert.Throws<Exception>(() => Test("( -name foobar* ( ( ) ) -true ) )"));
    }

    [Test]
    public void TrueAndFalse()
    {
        Test("-false",
            matches: null,
            mismatches: new[] {
                "foobar.baz",
                "foobar.faz",
            });

        Test("-true",
            matches: new[] {
                "foobar.baz",
                "foobar.faz",
            },
            mismatches: null);
    }
}