using System.Globalization;
using Clinisys.Application.Features.LaboratoryReports;
using Clinisys.Domain;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Clinisys.Infrastructure.LaboratoryReports;

public sealed class LaboratoryReportPdf(string laboratoryName) : ILaboratoryReportPdf
{
    static LaboratoryReportPdf()
    {
        // This SQL Express deployment is Windows. Arial is supplied by Windows;
        // do not copy proprietary font binaries into the repository.
        if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true;
    }

    public byte[] Generate(LaboratoryReportData report)
    {
        LaboratoryReportQueries.EnsureOfficial(report);
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException("This deployment requires the configured Windows PDF font environment.");
        var renderer = new PdfDocumentRenderer { Document = CreateDocument(report) };
        renderer.RenderDocument();
        using var stream = new MemoryStream();
        renderer.PdfDocument.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    // Kept internal to Infrastructure; tests inspect the real layout model as well
    // as reopening generated PDFs, without a separate template engine.
    private Document CreateDocument(LaboratoryReportData report)
    {
        var name = string.IsNullOrWhiteSpace(laboratoryName) ? "Laboratoire" : laboratoryName.Trim();
        if (name.Length > 200 || name.Any(char.IsControl))
            throw new InvalidOperationException("Invalid laboratory display configuration.");
        var document = new Document();
        document.Info.Title = "Compte rendu d'analyses médicales";
        document.Info.Author = name;
        var normal = document.Styles[StyleNames.Normal]!;
        normal.Font.Name = "Arial";
        normal.Font.Size = 9;
        normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(5);
        var section = document.AddSection();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = Orientation.Portrait;
        section.PageSetup.TopMargin = Unit.FromCentimeter(2.2);
        section.PageSetup.BottomMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.LeftMargin = Unit.FromCentimeter(1.8);
        section.PageSetup.RightMargin = Unit.FromCentimeter(1.8);
        var header = section.Headers.Primary.AddParagraph(name + " | Demande " + report.Number);
        header.Format.Font.Size = 8;
        header.Format.Font.Color = Colors.DarkSlateGray;
        var footer = section.Footers.Primary.AddParagraph("Confidentiel - Page ");
        footer.Format.Alignment = ParagraphAlignment.Center;
        footer.Format.Font.Size = 8;
        footer.AddPageField(); footer.AddText(" / "); footer.AddNumPagesField();

        var title = section.AddParagraph("COMPTE RENDU D'ANALYSES MÉDICALES");
        title.Format.Font.Size = 15; title.Format.Font.Bold = true;
        title.Format.Font.Color = Colors.DarkSlateGray;
        title.Format.SpaceAfter = Unit.FromPoint(14);
        section.AddParagraph("Demande : " + report.Number + " | Enregistrée le " + Date(report.RegisteredAt));
        section.AddParagraph("Patient : " + report.FirstName + " " + report.LastName);
        section.AddParagraph("Identifiant patient : " + report.PatientId.ToString());
        section.AddParagraph("Naissance : " + report.DateOfBirthSnapshot.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            + " | Sexe : " + (report.SexSnapshot switch { Sex.Female => "Féminin", Sex.Male => "Masculin", _ => "Non précisé" }));
        section.AddParagraph("Validation biologique : " + Date(report.ValidatedAt!.Value));
        section.AddParagraph("Biologiste validateur (identifiant enregistré) : " + report.ValidatedBy);
        section.AddParagraph("Statut : " + (report.Status == RequestStatus.Delivered ? "Délivrée" : "Validée"));

        var table = section.AddTable();
        table.Borders.Color = Colors.LightGray;
        table.Borders.Width = Unit.FromPoint(0.5);
        table.TopPadding = Unit.FromPoint(5); table.BottomPadding = Unit.FromPoint(5);
        foreach (var width in new[] { 5.0, 3.2, 1.7, 3.8, 3.7 }) table.AddColumn(Unit.FromCentimeter(width));
        var heading = table.AddRow();
        heading.HeadingFormat = true;
        heading.Shading.Color = Colors.LightGray;
        heading.Format.Font.Bold = true;
        var labels = new[] { "Analyse", "Résultat", "Unité", "Référence figée", "Interprétation" };
        for (var i = 0; i < labels.Length; i++) heading.Cells[i].AddParagraph(labels[i]);
        foreach (var analysis in report.Analyses)
        {
            var row = table.AddRow();
            row.VerticalAlignment = VerticalAlignment.Center;
            row.Cells[0].AddParagraph(analysis.Name + " (" + analysis.Code + ")");
            row.Cells[1].Format.Font.Size = 8;
            row.Cells[1].AddParagraph(Number(analysis.Value!.Value) + (analysis.IsCalculated ? " (calculé)" : ""));
            row.Cells[2].AddParagraph(analysis.Unit);
            row.Cells[3].Format.Font.Size = 8;
            row.Cells[3].AddParagraph(analysis.LowerBound is { } lower && analysis.UpperBound is { } upper
                ? Number(lower) + " à " + Number(upper) : "Non disponible");
            var interpretation = analysis.Abnormality switch
            {
                Abnormality.Low => "BAS - hors normes",
                Abnormality.High => "HAUT - hors normes",
                Abnormality.Normal => "Dans les normes",
                _ => "Non évalué"
            };
            row.Cells[4].AddParagraph(interpretation);
            if (analysis.Abnormality is Abnormality.Low or Abnormality.High)
            {
                row.Cells[1].Format.Font.Bold = true;
                row.Cells[4].Format.Font.Bold = true;
                row.Cells[4].Format.Font.Color = Colors.DarkRed;
            }
        }
        var note = section.AddParagraph("Bornes inclusives conservées lors de la saisie. L'absence de référence ne signifie pas un résultat normal.");
        note.Format.Font.Size = 8;
        note.Format.SpaceBefore = Unit.FromPoint(8);
        return document;
    }

    private static string Number(decimal value) => value.ToString("0.######", CultureInfo.GetCultureInfo("fr-FR"));
    private static string Date(DateTimeOffset instant) => instant.UtcDateTime.ToString("dd/MM/yyyy HH:mm 'UTC'", CultureInfo.InvariantCulture);
}
