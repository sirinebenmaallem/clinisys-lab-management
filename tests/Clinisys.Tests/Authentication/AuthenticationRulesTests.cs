using System.Globalization;
using System.Text.Json;
using Clinisys.Application.Features.Authentication;

namespace Clinisys.Tests.Authentication;

// Pure Application tests: no host, provider or connection is constructed.
public sealed class AuthenticationRulesTests
{
    private const string Password = "Synthetic password!";
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("abc", "abc", "ABC")]
    [InlineData("  User.Name_1-2  ", "User.Name_1-2", "USER.NAME_1-2")]
    [InlineData("0_a", "0_a", "0_A")]
    public void UserName_PreservesTrimmedDisplayAndNormalizesInvariantly(string input, string display, string normalized)
    {
        Assert.Equal(display, AuthenticationRules.ValidateUserName(input));
        Assert.Equal(normalized, AuthenticationRules.NormalizeUserName(input));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ab")]
    [InlineData(".abc")]
    [InlineData("_abc")]
    [InlineData("-abc")]
    [InlineData("a bc")]
    [InlineData("abc@domain")]
    [InlineData("abé")]
    [InlineData("abc\u0000")]
    [InlineData("abc/def")]
    public void InvalidUserName_IsRejectedWithoutEchoingInput(string? input)
    {
        var error = Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateUserName(input));
        Assert.Equal(AuthenticationError.Validation, error.Error);
        Assert.Equal("userName", error.Field);
        Assert.Equal("Authentication input is missing or invalid.", error.Message);
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void UserName_LengthLimitsApplyAfterTrimming(int length, bool valid)
    {
        var text = "  " + new string('a', length) + "  ";
        if (valid) Assert.Equal(new string('a', length), AuthenticationRules.ValidateUserName(text));
        else Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateUserName(text));
    }

    [Fact]
    public void Normalization_DoesNotUseCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("INITIAL", AuthenticationRules.NormalizeUserName("initial"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    public void Password_LengthPolicyDoesNotAddCompositionRules(int length, bool valid)
    {
        var text = new string('a', length);
        if (valid) Assert.Same(text, AuthenticationRules.ValidatePassword(text));
        else Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidatePassword(text));
    }

    [Fact]
    public void Login_RejectsMissingFieldsAndNeverTransformsPassword()
    {
        var password = "  Synthetic secret  ";
        var original = new LoginInput(" User.Name ", password);
        var validated = AuthenticationRules.ValidateLogin(original);
        Assert.Equal("User.Name", validated.UserName);
        Assert.Same(password, validated.Password);
        Assert.Equal(" User.Name ", original.UserName);
        Assert.DoesNotContain(password, original.ToString());
        Assert.DoesNotContain("User.Name", original.ToString());
        Assert.Equal(new string(' ', 15), AuthenticationRules.ValidatePassword(new string(' ', 15)));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateLogin(null));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateLogin(new("valid", null)));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateLogin(new(null, Password)));
    }

    [Theory]
    [InlineData("{\"userName\":\"synthetic\",\"password\":\"Synthetic password!\",\"roles\":[\"Biologist\"]}")]
    [InlineData("{\"userName\":\"synthetic\",\"password\":\"Synthetic password!\",\"userId\":\"synthetic\"}")]
    [InlineData("{\"userName\":\"synthetic\",\"password\":\"Synthetic password!\",\"rememberMe\":true}")]
    public void LoginJson_RejectsClientControlledIdentityAndRoleFields(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LoginInput>(json, Json));

    [Fact]
    public void JsonContracts_ContainOnlyApprovedFields()
    {
        var login = JsonSerializer.Deserialize<LoginInput>("{\"userName\":\"synthetic\",\"password\":\"Synthetic password!\"}", Json);
        Assert.Equal("synthetic", AuthenticationRules.ValidateLogin(login).UserName);
        Assert.NotNull(JsonSerializer.Deserialize<LogoutInput>("{}", Json));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<LogoutInput>("{\"userId\":\"synthetic\"}", Json));
        var session = new AuthenticationSessionDto(Guid.NewGuid(), "synthetic", [AuthenticationRoles.Technician], Now.AddMinutes(30));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(session, Json));
        Assert.Equal(new[] { "expiresAt", "roles", "userId", "userName" }, json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        using var token = JsonDocument.Parse(JsonSerializer.Serialize(new AntiforgeryTokenDto("synthetic-token", "X-CSRF-TOKEN"), Json));
        Assert.Equal(new[] { "headerName", "requestToken" }, token.RootElement.EnumerateObject().Select(x => x.Name).Order());
    }

    [Fact]
    public void Session_RequiresStableIdentityFutureUtcExpiryAndExplicitKnownRoles()
    {
        var roles = new List<string> { AuthenticationRoles.Technician, AuthenticationRoles.Technician };
        var session = new AuthenticationSessionDto(Guid.NewGuid(), "synthetic", roles, Now.AddMinutes(30));
        var validated = AuthenticationRules.ValidateSession(session, Now);
        Assert.Equal(session.UserId, validated.UserId);
        Assert.Single(validated.Roles);
        roles.Clear();
        Assert.Single(validated.Roles);
        session = validated; // Keep each following rejection independent of the mutated source list.
        foreach (var invalid in new[] { session with { UserId = Guid.Empty }, session with { Roles = [] },
            session with { Roles = ["Administrator"] }, session with { ExpiresAt = Now },
            session with { ExpiresAt = Now.AddSeconds(-1) }, session with { ExpiresAt = Now.AddHours(2).ToOffset(TimeSpan.FromHours(1)) } })
            Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateSession(invalid, Now));
        Assert.Throws<AuthenticationException>(() => AuthenticationRules.ValidateSession(null, Now));
    }

    [Fact]
    public void Application_DoesNotReferenceIdentityEfCoreOrAspNetCore()
    {
        var references = typeof(LoginInput).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, x => x.Name!.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
            || x.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || x.Name.StartsWith("Microsoft.Extensions.Identity", StringComparison.Ordinal)
            || x.Name == "System.Web");
    }
}
