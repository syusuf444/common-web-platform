using Platform.Modules.Identity.Application;

namespace Platform.UnitTests;

public sealed class RegistrationRequestValidatorTests
{
    [Fact]
    public void AcceptsValidRegistrationRequest()
    {
        var errors = RegistrationRequestValidator.Validate(
            new RegisterRequest("dev@example.test", "Developer", "CWP-Local-Password-938!"));

        Assert.Empty(errors);
    }

    [Fact]
    public void RejectsInvalidEmailDisplayNameAndShortPassword()
    {
        var errors = RegistrationRequestValidator.Validate(new RegisterRequest("not-an-email", "  ", "short"));

        Assert.Contains(nameof(RegisterRequest.Email), errors.Keys);
        Assert.Contains(nameof(RegisterRequest.DisplayName), errors.Keys);
        Assert.Contains(nameof(RegisterRequest.Password), errors.Keys);
    }

    [Fact]
    public void RejectsDisplayNameLongerThanSchemaLimit()
    {
        var errors = RegistrationRequestValidator.Validate(
            new RegisterRequest("dev@example.test", new string('x', 121), "CWP-Local-Password-938!"));

        Assert.Contains(nameof(RegisterRequest.DisplayName), errors.Keys);
    }
}
