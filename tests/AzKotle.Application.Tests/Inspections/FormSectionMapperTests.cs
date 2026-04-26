using AzKotle.Application.Abstractions;
using AzKotle.Application.Inspections;
using AzKotle.Domain.Entities.Inspections;
using FluentAssertions;
using NSubstitute;

namespace AzKotle.Application.Tests.Inspections;

[Trait("Category", "Unit")]
public class FormSectionMapperTests
{
    private static InspectionTemplate Nv191StubTemplate() => new(
        Id: "nv191_2022",
        Version: "1.0.0",
        Title: "Roční prohlídka",
        Sections: new[]
        {
            new InspectionTemplateSection(
                Id: "fuel_supply",
                Title: "Přívod paliva",
                Fields: new[]
                {
                    new InspectionTemplateField("gas_meter_reading_m3", "Stav plynoměru (m³)", "number", null),
                    new InspectionTemplateField("main_valve_accessible", "Hlavní uzávěr přístupný", "boolean", null),
                }),
            new InspectionTemplateSection(
                Id: "photos",
                Title: "Fotografie",
                Fields: new[]
                {
                    new InspectionTemplateField("photo_burner", "Foto hořáku", "photo", null),
                }),
            new InspectionTemplateSection(
                Id: "summary",
                Title: "Závěr",
                Fields: new[]
                {
                    new InspectionTemplateField("next_due_at", "Termín další revize", "date", null),
                    new InspectionTemplateField("technician_signature", "Podpis technika", "signature", null),
                }),
        });

    private static FormSectionMapper MapperWithNv191Template()
    {
        var provider = Substitute.For<IInspectionTemplateProvider>();
        provider.GetTemplate(InspectionType.AnnualNv191).Returns(Nv191StubTemplate());
        provider.GetTemplate(Arg.Is<InspectionType>(t => t != InspectionType.AnnualNv191)).Returns((InspectionTemplate?)null);
        return new FormSectionMapper(provider);
    }

    [Fact]
    public void Map_Nv191_ProducesSectionsWithSchemaLabels()
    {
        var mapper = MapperWithNv191Template();
        var json = """
        {
          "gas_meter_reading_m3": 12345.6,
          "main_valve_accessible": true,
          "photo_burner": "boilers/123/photos/burner.jpg",
          "next_due_at": "2027-04-25"
        }
        """;

        var sections = mapper.Map(InspectionType.AnnualNv191, json);

        sections.Should().HaveCount(3);
        sections[0].Title.Should().Be("Přívod paliva");
        sections[0].Fields.Should().SatisfyRespectively(
            f => { f.Label.Should().Be("Stav plynoměru (m³)"); f.DisplayValue.Should().Be("12345.6"); },
            f => { f.Label.Should().Be("Hlavní uzávěr přístupný"); f.DisplayValue.Should().Be("Ano"); });
    }

    [Fact]
    public void Map_Nv191_BooleanFalse_RendersAsNe()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"main_valve_accessible": false}""");

        var field = sections.Should().ContainSingle(s => s.Title == "Přívod paliva")
            .Which.Fields.Should().ContainSingle(f => f.Label == "Hlavní uzávěr přístupný").Subject;
        field.DisplayValue.Should().Be("Ne");
    }

    [Fact]
    public void Map_Nv191_PhotoFieldWithValue_RendersAsAttached()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"photo_burner": "s3-key.jpg"}""");

        var photoField = sections.Should().ContainSingle(s => s.Title == "Fotografie")
            .Which.Fields.Should().ContainSingle().Subject;
        photoField.DisplayValue.Should().Be("Připojeno");
    }

    [Fact]
    public void Map_Nv191_PhotoFieldEmpty_RendersAsDash()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"photo_burner": ""}""");

        sections.Should().ContainSingle(s => s.Title == "Fotografie")
            .Which.Fields.Should().ContainSingle()
            .Which.DisplayValue.Should().Be("—");
    }

    [Fact]
    public void Map_Nv191_MissingField_RendersAsDash()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"main_valve_accessible": true}""");

        var meterField = sections.Should().ContainSingle(s => s.Title == "Přívod paliva")
            .Which.Fields.Should().ContainSingle(f => f.Label == "Stav plynoměru (m³)").Subject;
        meterField.DisplayValue.Should().Be("—");
    }

    [Fact]
    public void Map_Nv191_DateField_FormatsAsCzech()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"next_due_at": "2027-04-25"}""");

        var dateField = sections.Should().ContainSingle(s => s.Title == "Závěr")
            .Which.Fields.Should().ContainSingle().Subject;
        dateField.Label.Should().Be("Termín další revize");
        dateField.DisplayValue.Should().Be("25.04.2027");
    }

    [Fact]
    public void Map_Nv191_SignatureField_IsExcluded()
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, """{"technician_signature": "base64data"}""");

        var summarySection = sections.SingleOrDefault(s => s.Title == "Závěr");
        if (summarySection is not null)
        {
            summarySection.Fields.Should().NotContain(f => f.Label.Contains("Podpis"));
        }
    }

    [Fact]
    public void Map_NonNv191Type_FallsBackToHumanizedFlatList()
    {
        var mapper = MapperWithNv191Template();
        var json = """{"some_random_field": 42, "another_one": "value"}""";

        var sections = mapper.Map(InspectionType.Emergency, json);

        sections.Should().ContainSingle()
            .Which.Title.Should().Be("Vyplněné údaje");
        sections[0].Fields.Should().HaveCount(2);
        sections[0].Fields[0].Label.Should().Be("Some random field");
        sections[0].Fields[0].DisplayValue.Should().Be("42");
        sections[0].Fields[1].Label.Should().Be("Another one");
        sections[0].Fields[1].DisplayValue.Should().Be("value");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not-valid-json")]
    public void Map_EmptyOrInvalidJson_ReturnsEmpty(string? json)
    {
        var mapper = MapperWithNv191Template();
        var sections = mapper.Map(InspectionType.AnnualNv191, json);

        sections.Should().BeEmpty();
    }

    [Fact]
    public void Map_Nv191_SectionWithOnlySignatureField_IsOmitted()
    {
        var provider = Substitute.For<IInspectionTemplateProvider>();
        provider.GetTemplate(InspectionType.AnnualNv191).Returns(new InspectionTemplate(
            Id: "nv191_2022",
            Version: "1.0.0",
            Title: "Test",
            Sections: new[]
            {
                new InspectionTemplateSection("sig_only", "Pouze podpis",
                    new[] { new InspectionTemplateField("sig", "Podpis", "signature", null) }),
            }));
        var mapper = new FormSectionMapper(provider);

        var sections = mapper.Map(InspectionType.AnnualNv191, """{"sig":"x"}""");

        sections.Should().BeEmpty();
    }
}
