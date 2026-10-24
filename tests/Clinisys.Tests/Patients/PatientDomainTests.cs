using Clinisys.Application.Features.Patients;
using Clinisys.Domain;
using Clinisys.Domain.Entities;

namespace Clinisys.Tests.Patients;

public sealed class PatientDomainTests
{
    [Fact]
    public void CreateAndUpdate_NormalizeFieldsAndPreserveIdentity()
    {
        var p = new Patient(" Synthetic ", " Test ", new(2000, 1, 1), Sex.Unspecified, " syn-id ", " SYN-PHONE ");
        Assert.Equal("Synthetic", p.FirstName);
        Assert.Equal("Test", p.LastName);
        Assert.Equal("SYN-ID", p.NationalIdentityNumber);
        Assert.Equal("SYN-PHONE", p.PhoneNumber);
        var id = p.Id;
        p.Update("Changed", "Synthetic", DateOnly.FromDateTime(DateTime.UtcNow), Sex.Female, phoneNumber: "SHARED-TEST");
        Assert.Equal(id, p.Id);
        Assert.Null(p.NationalIdentityNumber);
        p.Update("Changed", "Synthetic", new(2000, 1, 1), Sex.Male, "SYN-ID");
        Assert.Null(p.PhoneNumber);
    }

    [Theory]
    [InlineData("", "Test")]
    [InlineData("Synthetic", " ")]
    [InlineData(null, "Test")]
    public void NamesAreRequired(string? first, string last) => Assert.ThrowsAny<ArgumentException>(() =>
        new Patient(first!, last, new(2000, 1, 1), Sex.Unspecified, phoneNumber: "TEST-ONLY"));

    [Fact]
    public void InvalidUpdate_IsAtomic()
    {
        var p = new Patient("Original", "Synthetic", new(2000, 1, 1), Sex.Male, "ORIGINAL-ID", "TEST-ONLY");
        foreach (var update in new Action[]
        {
            () => p.Update("Changed", "Changed", default, Sex.Female, "NEW-ID"),
            () => p.Update("Changed", "Changed", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1), Sex.Female, "NEW-ID"),
            () => p.Update("Changed", "Changed", new(2000, 1, 1), (Sex)99, "NEW-ID"),
            () => p.Update("Changed", "Changed", new(2000, 1, 1), Sex.Female),
            () => p.Update("Changed", "Changed", new(2000, 1, 1), Sex.Female, " "),
            () => p.Update(new string('x', 101), "Changed", new(2000, 1, 1), Sex.Female, "NEW-ID"),
            () => p.Update("Changed", new string('x', 101), new(2000, 1, 1), Sex.Female, "NEW-ID"),
            () => p.Update("Changed", "Changed", new(2000, 1, 1), Sex.Female, new string('x', 51)),
            () => p.Update("Changed", "Changed", new(2000, 1, 1), Sex.Female, "NEW-ID", new string('x', 31))
        })
        {
            Assert.ThrowsAny<ArgumentException>(update);
            Assert.Equal("Original", p.FirstName);
            Assert.Equal("Synthetic", p.LastName);
            Assert.Equal(new DateOnly(2000, 1, 1), p.DateOfBirth);
            Assert.Equal(Sex.Male, p.Sex);
            Assert.Equal("ORIGINAL-ID", p.NationalIdentityNumber);
            Assert.Equal("TEST-ONLY", p.PhoneNumber);
        }
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(1, 101)]
    [InlineData(int.MaxValue, 100)]
    public void PaginationRejectsInvalidBounds(int page, int size) =>
        Assert.Throws<PatientException>(() => new PatientPageQuery { Page = page, PageSize = size }.Validate());
}
