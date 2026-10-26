using System.Reflection;
using Clinisys.API.Authentication;
using Clinisys.Application.Features.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Clinisys.Tests.Authentication;

// Pure diagnostic formatting only: never run the interactive command or resolve a DbContext.
public sealed class LocalUserAdministrationTests
{
    private static string Reason(Exception error) => (string)typeof(LocalUserAdministration)
        .GetMethod("SafeFailureReason", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [error])!;

    [Fact]
    public void PasswordValidationFailure_ExplainsBoundsWithoutIncludingInput()
    {
        var message = Reason(AuthenticationException.Validation("password"));
        Assert.Contains("15", message);
        Assert.Contains("128", message);
        Assert.Contains("masquée", message);
    }

    [Theory]
    [InlineData("InvalidPassword", "Mot de passe refusé")]
    [InlineData("PasswordTooShort", "Mot de passe refusé")]
    [InlineData("DuplicateUserName", "déjà utilisé")]
    [InlineData("ConcurrencyFailure", "simultanément")]
    [InlineData("Untrusted-SYNTHETIC-SECRET", "Opération refusée par Identity")]
    public void IdentityFailures_UseOnlyControlledMessages(string code, string expected)
    {
        var result = IdentityResult.Failed(new IdentityError { Code = code, Description = "SYNTHETIC-SECRET hash token connection" });
        var invocation = Assert.Throws<TargetInvocationException>(() => typeof(LocalUserAdministration)
            .GetMethod("Require", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [result]));
        var message = Reason(invocation.InnerException!);
        Assert.Contains(expected, message);
        Assert.DoesNotContain("SYNTHETIC-SECRET", message);
        Assert.DoesNotContain("hash token connection", message);
    }

    [Fact]
    public void TechnicalFailure_DoesNotExposeExceptionMessagesOrInnerMessages()
    {
        var message = Reason(new InvalidOperationException("SYNTHETIC-SECRET", new Exception("SYNTHETIC-INNER")));
        Assert.DoesNotContain("SYNTHETIC", message);
        Assert.Contains("technique", message);
    }

    [Fact]
    public void SuccessfulIdentityOperation_DoesNotThrow() => typeof(LocalUserAdministration)
        .GetMethod("Require", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [IdentityResult.Success]);
}
