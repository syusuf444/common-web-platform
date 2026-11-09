namespace Platform.UnitTests;

public sealed class SampleRequestValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingOrWhitespaceNames(string? name)
    {
        var errors = SampleRequestValidator.Validate(new SampleRequest(name));

        Assert.Contains("Name", errors.Keys);
        Assert.Equal("Name is required.", Assert.Single(errors["Name"]));
    }

    [Fact]
    public void RejectsNamesLongerThanOneHundredCharacters()
    {
        var errors = SampleRequestValidator.Validate(new SampleRequest(new string('a', 101)));

        Assert.Equal("Name must be 100 characters or fewer.", Assert.Single(errors["Name"]));
    }

    [Fact]
    public void AcceptsTrimmedNameAtMaximumLength()
    {
        var errors = SampleRequestValidator.Validate(new SampleRequest(new string('a', 100)));

        Assert.Empty(errors);
    }
}
