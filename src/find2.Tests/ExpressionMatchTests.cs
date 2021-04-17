using NUnit.Framework;

namespace find2.Tests
{
    public class ExpressionMatchTests
    {
        private static WindowsFileEntry File(string name, bool toUpper = false)
        {
            if (toUpper) name = name?.ToUpper();
            return new WindowsFileEntry(false, name);
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void NoGlob(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "foobar");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsFalse(matcher(File("notfoobar", toUpper)));
            Assert.IsFalse(matcher(File("foobarnot", toUpper)));
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void StartingGlob(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "*foobar");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsTrue(matcher(File("notfoobar", toUpper)));
            Assert.IsFalse(matcher(File("foobarnot", toUpper)));
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void EndingGlob(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "foobar*");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsFalse(matcher(File("notfoobar", toUpper)));
            Assert.IsTrue(matcher(File("foobarnot", toUpper)));
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void StartingAndEndingGlob(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "*foobar*");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsTrue(matcher(File("notfoobar", toUpper)));
            Assert.IsTrue(matcher(File("foobarnot", toUpper)));
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void CenterGlobs(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "*foo*bar*");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsTrue(matcher(File("notfoobar", toUpper)));
            Assert.IsTrue(matcher(File("foobarnot", toUpper)));
            Assert.IsTrue(matcher(File("foonotbar", toUpper)));
        }

        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void MultipleExpressions(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build(param, "*foo*bar*", param, "*foo*bar*", param, "*foo*bar*");
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsTrue(matcher(File("notfoobar", toUpper)));
            Assert.IsTrue(matcher(File("foobarnot", toUpper)));
            Assert.IsTrue(matcher(File("foonotbar", toUpper)));
        }


        [Test]
        [TestCase("-name", false)]
        [TestCase("-iname", true)]
        public void EmptyAlwaysMatches(string param, bool toUpper)
        {
            var matcher = ExpressionMatch.Build();
            Assert.IsTrue(matcher(File("foobar", toUpper)));
            Assert.IsTrue(matcher(File("notfoobar", toUpper)));
            Assert.IsTrue(matcher(File("foobarnot", toUpper)));
            Assert.IsTrue(matcher(File("foonotbar", toUpper)));
        }

        [Test]
        [TestCase("!")]
        [TestCase("-not")]
        public void NotInvertsMatch(string param)
        {
            var matcher = ExpressionMatch.Build(param, "-name", "foobar");
            Assert.IsFalse(matcher(File("foobar")));
            Assert.IsTrue(matcher(File("notfoobar")));
            Assert.IsTrue(matcher(File("foobarnot")));
        }

        [Test]
        [TestCase("-or")]
        [TestCase("-o")]
        public void OrOperator(string param)
        {
            var matcher = ExpressionMatch.Build("-name", "foobar", param, "-name", "foobar2");
            Assert.IsTrue(matcher(File("foobar")));
            Assert.IsTrue(matcher(File("foobar2")));
            Assert.IsFalse(matcher(File("notfoobar")));
            Assert.IsFalse(matcher(File("foobarnot")));
        }

        [Test]
        public void Parentheses()
        {
            var matcher = ExpressionMatch.Build("-name", "foobar*", "-and", "(", "-name", "*baz", "-or", "-name", "*faz", ")");
            Assert.IsTrue(matcher(File("foobar baz")));
            Assert.IsTrue(matcher(File("foobar faz")));
            Assert.IsFalse(matcher(File("foobar")));
            Assert.IsFalse(matcher(File("notfoobar bazz")));
            Assert.IsFalse(matcher(File("foobarnot")));

            // Starting/ending parentheses are simplified out because they'd need to be special cased if not.
            matcher = ExpressionMatch.Build("(", "-name", "foobar*", ")");
            Assert.IsTrue(matcher(File("foobar baz")));
            Assert.IsTrue(matcher(File("foobar faz")));
        }
    }
}
