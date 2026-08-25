using LibTreeMapView.Core.Symbols;

namespace LibTreeMapView.Core.Tests;

public class SymbolNameParserTests
{
    [Fact]
    public void Split_GlobalNameHasNoNamespace()
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split("alpha_add");

        Assert.Empty(path);
        Assert.Equal("alpha_add", leaf);
    }

    [Fact]
    public void Split_NestedNamespaces()
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split("app::net::Client::Send");

        Assert.Equal(["app", "net", "Client"], path);
        Assert.Equal("Send", leaf);
    }

    [Fact]
    public void Split_KeepsTemplateArgumentsTogether()
    {
        (IReadOnlyList<string> path, string leaf) =
            SymbolNameParser.Split("std::basic_string<char,std::char_traits<char>,std::allocator<char> >::append");

        Assert.Equal(["std", "basic_string<char,std::char_traits<char>,std::allocator<char> >"], path);
        Assert.Equal("append", leaf);
    }

    [Fact]
    public void Split_KeepsTemplateArgumentsOfTheLeaf()
    {
        (IReadOnlyList<string> path, string leaf) =
            SymbolNameParser.Split("std::vector<int>::emplace_back<int,int>");

        Assert.Equal(["std", "vector<int>"], path);
        Assert.Equal("emplace_back<int,int>", leaf);
    }

    [Theory]
    [InlineData("std::ostream::operator<<", "operator<<")]
    [InlineData("app::Value::operator<", "operator<")]
    [InlineData("app::Value::operator>=", "operator>=")]
    [InlineData("app::Value::operator<=>", "operator<=>")]
    public void Split_HandlesComparisonOperators(string qualifiedName, string expectedLeaf)
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split(qualifiedName);

        Assert.Equal(expectedLeaf, leaf);
        Assert.Equal(qualifiedName[..^(expectedLeaf.Length + 2)].Split("::"), path);
    }

    [Fact]
    public void Split_HandlesArrowOperator()
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split("app::Ptr::operator->");

        Assert.Equal(["app", "Ptr"], path);
        Assert.Equal("operator->", leaf);
    }

    [Fact]
    public void Split_IgnoresColonsInsideParameters()
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split("app::Handler<void (app::Event::*)()>::Run");

        Assert.Equal(["app", "Handler<void (app::Event::*)()>"], path);
        Assert.Equal("Run", leaf);
    }

    [Fact]
    public void Split_EmptyName()
    {
        (IReadOnlyList<string> path, string leaf) = SymbolNameParser.Split(string.Empty);

        Assert.Empty(path);
        Assert.Equal(string.Empty, leaf);
    }
}
