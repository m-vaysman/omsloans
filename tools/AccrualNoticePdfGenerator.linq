<Query Kind="Program">
  <NuGetReference Version="2024.10.2">QuestPDF</NuGetReference>
  <Namespace>QuestPDF.Fluent</Namespace>
  <Namespace>QuestPDF.Helpers</Namespace>
  <Namespace>QuestPDF.Infrastructure</Namespace>
  <Namespace>System.Globalization</Namespace>
</Query>

// LINQPad script – Accrual Notice PDF Generator with 5 Templates
//
// Generates mock agent-bank accrual notices as PDFs, for feeding the watched folder and for
// exercising extraction against varied layouts. Payment details are randomly generated and
// fake; nothing here is real.
//
// NuGet (QuestPDF) and namespaces are declared in the Query header above rather than with
// #r / using directives, which is how LINQPad resolves them.

// ============================================================
// 1. Data Models
// ============================================================

public class AccrualData
{
	public string FacilityId { get; set; } = "LN226909";
	public string Borrower { get; set; } = "ACCO Brands Corporation";
	public DateTime PeriodStart { get; set; } = new DateTime(2006, 10, 26);
	public DateTime PeriodEnd { get; set; } = new DateTime(2006, 11, 9);
	public int Days { get; set; } = 14;
	public string DayCount { get; set; } = "Actual/360";
	public decimal BaseRate { get; set; } = 5.37m;
	public decimal Margin { get; set; } = 1.75m;
	public decimal AllInRate => BaseRate + Margin;
	public decimal BeginningPrincipal { get; set; } = 106_000_000m;
	public decimal PrincipalPaydown { get; set; } = 0m;
	public decimal EndingPrincipal => BeginningPrincipal - PrincipalPaydown;
	public decimal AccruedInterest { get; set; } = 293_502.22m;
	public DateTime PaymentDate { get; set; } = new DateTime(2007, 1, 19);
	public string ActivityType { get; set; } = "Interest Accrual";
}

public class BankDetails
{
	public string BankName { get; set; }
	public string AddressLine1 { get; set; }
	public string AddressLine2 { get; set; }
	public string CityStateZip { get; set; }
	public string Telephone { get; set; }
	public string Email { get; set; }
	public string Department { get; set; } = "Loan Operations / Agency Services";
}

// ============================================================
// 2. Fake Payment Instructions Generator
// ============================================================

public static class FakePaymentFactory
{
	static readonly Random Rng = new Random();

	public static (string Aba, string Account, string AccountName, string Swift, string Reference) Create(BankDetails bank, AccrualData data)
	{
		string aba = $"{Rng.Next(100, 999):000}{Rng.Next(10000, 99999):00000}";
		string account = Rng.NextInt64(1000000000, 9999999999).ToString();
		string swift = $"{new string(Enumerable.Range(0, 4).Select(_ => (char)Rng.Next('A', 'Z' + 1)).ToArray())}US33XXX";
		string reference = $"{data.FacilityId} / Interest {data.PeriodStart:MMM yyyy}";

		return (aba, account, $"{data.Borrower} – Loan Collection Account", swift, reference);
	}
}

// ============================================================
// 3. Watermark
// ============================================================

public const string WatermarkLine1 = "COBBLER HILL DEV";
public const string WatermarkLine2 = "(MOCK)";

/// <summary>Large diagonal watermark, applied to every template.</summary>
/// <remarks>
/// Three things this layout is working around, all learned the hard way from a first
/// attempt that rendered as a cropped, fragmented diagonal:
///
/// 1. Foreground, not Background. Table cells paint solid fills (white and #D6E3F0), so a
///    watermark drawn behind the content was hidden everywhere a row covered it and only
///    survived in whitespace — it read as broken fragments rather than one mark. Drawing on
///    top keeps it continuous. The alpha is what stops it obscuring anything: at ~13% the
///    rates and amounts underneath stay legible to a vision model, which is the whole point
///    of generating these.
///
/// 2. Two short lines, not one long one. Rotate() does not resize the layout box, so a
///    23-character string laid out at page width and then turned 45 degrees ran off the
///    corner and lost its first five characters. Splitting the text roughly halves the
///    diagonal it needs.
///
/// 3. Rotate() pivots on the top-left corner, not the centre, so the mark drifted up and to
///    the right. Translating the centre to the origin, rotating, and translating back is
///    what actually centres it.
/// </remarks>
static void ApplyWatermark(PageDescriptor page)
{
	// Width must clear the longest line at this font size or it wraps, and height must clear
	// both lines or the second one is silently clipped. Rotated 45 degrees a 470x120 block
	// occupies about 417pt on each axis, so it still sits well inside Letter (612x792).
	const float blockWidth = 470f;
	const float blockHeight = 120f;
	const float fontSize = 36f;

	// Black at ~13% alpha rather than a light grey: it stays visible on both the white body
	// and the tinted table rows, where a flat grey washes out against one or the other.
	var watermarkColor = Color.FromARGB(0x22, 0x00, 0x00, 0x00);

	// Applied outside the rotation, so these are plain page-space points that slide the
	// finished mark onto the page centre. Measured against the rendered output rather than
	// derived: the compensation depends on how Rotate() composes with the alignment around
	// it, and the arithmetic is easier to get wrong than to check.
	const float centringOffsetX = 263f;
	const float centringOffsetY = -125f;

	page.Foreground()
		.AlignCenter()
		.AlignMiddle()
		.Width(blockWidth)
		.Height(blockHeight)
		.TranslateX(centringOffsetX, Unit.Point)
		.TranslateY(centringOffsetY, Unit.Point)
		.Rotate(-45)
		.TranslateX(-blockWidth / 2, Unit.Point)
		.TranslateY(-blockHeight / 2, Unit.Point)
		.Column(col =>
		{
			col.Item().AlignCenter().Text(WatermarkLine1)
				.FontSize(fontSize).Bold().FontColor(watermarkColor);
			col.Item().AlignCenter().Text(WatermarkLine2)
				.FontSize(fontSize).Bold().FontColor(watermarkColor);
		});
}

// ============================================================
// 4. Main Generator Method
// ============================================================

public static byte[] GenerateNotice(AccrualData data, BankDetails bank, int templateNumber)
{
	if (templateNumber < 1 || templateNumber > 5)
		throw new ArgumentOutOfRangeException(nameof(templateNumber), "Template must be 1–5");

	var payment = FakePaymentFactory.Create(bank, data);

	var document = Document.Create(container =>
	{
		container.Page(page =>
		{
			page.Size(PageSizes.Letter);
			page.Margin(0.6f, Unit.Inch);
			page.DefaultTextStyle(x => x.FontSize(9.5f).FontFamily("Arial"));

			ApplyWatermark(page);

			switch (templateNumber)
			{
				case 1: BuildTemplate1_ClassicBlue(page, data, bank, payment); break;
				case 2: BuildTemplate2_ModernDark(page, data, bank, payment); break;
				case 3: BuildTemplate3_ConservativeGreen(page, data, bank, payment); break;
				case 4: BuildTemplate4_BoldFormal(page, data, bank, payment); break;
				case 5: BuildTemplate5_ElegantSerif(page, data, bank, payment); break;
			}
		});
	});

	return document.GeneratePdf();
}

// ============================================================
// TEMPLATE 1 – Classic Corporate Blue (Meridian style)
// ============================================================
static void BuildTemplate1_ClassicBlue(PageDescriptor page, AccrualData d, BankDetails b,
	(string Aba, string Account, string AccountName, string Swift, string Reference) p)
{
	var blue = "#1F4E79";
	var lightBlue = "#D6E3F0";

	page.Header().Column(col =>
	{
		col.Item().AlignCenter().Text(b.BankName).Bold().FontSize(16).FontColor(blue);
		col.Item().AlignCenter().Text(b.Department).FontSize(8).FontColor("#555555");
		col.Item().AlignCenter().Text($"{b.AddressLine1}  •  {b.CityStateZip}").FontSize(8);
		col.Item().AlignCenter().Text($"Tel: {b.Telephone}  •  {b.Email}").FontSize(8);
		col.Item().PaddingTop(4).LineHorizontal(2).LineColor(blue);
	});

	page.Content().PaddingTop(12).Column(col =>
	{
		col.Item().AlignCenter().Text("NOTICE OF INTEREST ACCRUAL AND PAYMENT").Bold().FontSize(12).FontColor(blue);
		col.Item().AlignCenter().Text("CONFIDENTIAL — FOR ADDRESSEE ONLY").FontSize(8).FontColor("#A00").Bold();
		col.Item().PaddingVertical(8);

		// Meta box
		col.Item().Background(lightBlue).Padding(6).Row(r =>
		{
			r.RelativeItem().Text($"Date of Notice: {DateTime.Today:MMMM d, yyyy}");
			r.RelativeItem().AlignRight().Text($"Facility ID: {d.FacilityId}");
		});

		col.Item().PaddingTop(10).Text($"TO: {d.Borrower}").Bold();
		col.Item().Text("Attention: Treasury / Loan Administration");

		col.Item().PaddingTop(8).Text(txt =>
		{
			txt.Span("RE: ").Bold();
			txt.Span($"Interest Accrual Notice – Facility {d.FacilityId}");
		});

		col.Item().PaddingTop(10).Text("Ladies and Gentlemen:");
		col.Item().Text("Pursuant to the Credit Agreement, we hereby notify you of the interest that has accrued for the Interest Period specified below.");

		// Rate table
		col.Item().PaddingTop(12).Table(t =>
		{
			t.ColumnsDefinition(c => { c.RelativeColumn(2.2f); c.RelativeColumn(1.3f); });
			t.Header(h =>
			{
				h.Cell().Background(blue).Padding(4).Text("Item").FontColor(Colors.White).Bold();
				h.Cell().Background(blue).Padding(4).AlignRight().Text("Detail").FontColor(Colors.White).Bold();
			});

			void Row(string label, string value, bool highlight = false)
			{
				// Typed explicitly: in QuestPDF 2024.10 Colors.White is a Color, and string and
				// Color convert to each other, so `var` leaves the ternary with no natural type.
				Color bg = highlight ? Color.FromHex("#FFF3CD") : Colors.White;
				t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Background(bg).Padding(4).Text(label);
				t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Background(bg).Padding(4).AlignRight().Text(value).Bold();
			}

			Row("Interest Period", $"{d.PeriodStart:MMM d, yyyy} – {d.PeriodEnd:MMM d, yyyy}");
			Row("Days / Day Count", $"{d.Days} days  /  {d.DayCount}");
			Row("Base Rate", $"{d.BaseRate:0.00}%");
			Row("Applicable Margin", $"{d.Margin:0.00}%");
			Row("All-in Rate", $"{d.AllInRate:0.00}%", true);
			Row("Outstanding Principal", d.BeginningPrincipal.ToString("C"));
			Row("Accrued Interest", d.AccruedInterest.ToString("C"), true);
			Row("Payment Date", d.PaymentDate.ToString("MMMM d, yyyy"));
		});

		// Payment instructions
		col.Item().PaddingTop(14).Text("PAYMENT INSTRUCTIONS").Bold().FontColor(blue);
		col.Item().Text($"Please remit {d.AccruedInterest:C} by wire transfer to:");

		col.Item().PaddingTop(6).Table(t =>
		{
			t.ColumnsDefinition(c => { c.RelativeColumn(1.4f); c.RelativeColumn(3f); });
			void P(string k, string v) { t.Cell().Background(lightBlue).Padding(3).Text(k).Bold(); t.Cell().Padding(3).Text(v); }
			P("Bank Name", b.BankName);
			P("ABA / Routing", p.Aba);
			P("Account Number", p.Account);
			P("Account Name", p.AccountName);
			P("SWIFT", p.Swift);
			P("Reference", p.Reference);
		});

		col.Item().PaddingTop(16).Text("Very truly yours,");
		col.Item().PaddingTop(20).Text(b.BankName).Bold();
		col.Item().Text("as Administrative Agent");
		col.Item().PaddingTop(8).Text("Authorized Signatory – Loan Operations");
	});

	page.Footer().AlignCenter().Text("This is a MOCK notice generated for demonstration purposes only.").FontSize(7).FontColor("#888");
}

// ============================================================
// TEMPLATE 2 – Modern Dark Header
// ============================================================
static void BuildTemplate2_ModernDark(PageDescriptor page, AccrualData d, BankDetails b,
	(string Aba, string Account, string AccountName, string Swift, string Reference) p)
{
	page.Header().Background("#1A1A2E").Padding(12).Column(c =>
	{
		c.Item().Text(b.BankName).FontSize(18).Bold().FontColor(Colors.White);
		c.Item().Text($"{b.Department}  |  {b.Telephone}  |  {b.Email}").FontSize(8).FontColor("#AAAAAA");
	});

	page.Content().PaddingTop(15).Column(col =>
	{
		col.Item().Text("INTEREST ACCRUAL NOTICE").Bold().FontSize(14);
		col.Item().Text($"Facility {d.FacilityId}  •  {d.Borrower}").FontSize(9).FontColor("#555");

		col.Item().PaddingTop(12).Row(r =>
		{
			r.RelativeItem().Column(c =>
			{
				c.Item().Text("Period").FontSize(8).FontColor("#888");
				c.Item().Text($"{d.PeriodStart:dd MMM yyyy} → {d.PeriodEnd:dd MMM yyyy}").Bold();
			});
			r.RelativeItem().Column(c =>
			{
				c.Item().Text("All-in Rate").FontSize(8).FontColor("#888");
				c.Item().Text($"{d.AllInRate:0.00}%").Bold().FontSize(12);
			});
			r.RelativeItem().Column(c =>
			{
				c.Item().Text("Amount Due").FontSize(8).FontColor("#888");
				c.Item().Text(d.AccruedInterest.ToString("C")).Bold().FontSize(12).FontColor("#C0392B");
			});
		});

		col.Item().PaddingTop(15).LineHorizontal(1).LineColor("#EEE");

		col.Item().PaddingTop(10).Text("Rate Detail").Bold();
		col.Item().Text($"Base {d.BaseRate:0.00}% + Margin {d.Margin:0.00}%   |   {d.Days} days Actual/360");
		col.Item().Text($"Principal: {d.BeginningPrincipal:C}");

		col.Item().PaddingTop(14).Background("#F8F9FA").Padding(10).Column(c =>
		{
			c.Item().Text("WIRE INSTRUCTIONS").Bold().FontSize(9);
			c.Item().Text($"{b.BankName}");
			c.Item().Text($"ABA {p.Aba}  •  Acct {p.Account}");
			c.Item().Text($"Ref: {p.Reference}");
		});

		col.Item().PaddingTop(20).Text("Regards,");
		col.Item().Text(b.BankName).Bold();
	});
}

// ============================================================
// TEMPLATE 3 – Conservative Green
// ============================================================
static void BuildTemplate3_ConservativeGreen(PageDescriptor page, AccrualData d, BankDetails b,
	(string Aba, string Account, string AccountName, string Swift, string Reference) p)
{
	var green = "#1B4F3C";

	page.Header().Column(c =>
	{
		c.Item().AlignCenter().Text(b.BankName.ToUpper()).Bold().FontSize(13).FontColor(green);
		c.Item().AlignCenter().Text($"{b.AddressLine1}, {b.CityStateZip}").FontSize(8);
		c.Item().PaddingTop(3).LineHorizontal(1.5f).LineColor(green);
	});

	page.Content().PaddingTop(10).Column(col =>
	{
		col.Item().AlignCenter().Text("NOTICE OF INTEREST ACCRUAL").Bold().FontSize(11).FontColor(green);
		col.Item().PaddingTop(8);

		col.Item().Text($"Borrower: {d.Borrower}");
		col.Item().Text($"Facility: {d.FacilityId}");
		col.Item().Text($"Interest Period: {d.PeriodStart:MMMM d, yyyy} to {d.PeriodEnd:MMMM d, yyyy} ({d.Days} days)");

		col.Item().PaddingTop(12).Text("Calculation").Bold().FontColor(green);
		col.Item().Text($"Principal × Rate × Days/360 = {d.BeginningPrincipal:C} × {d.AllInRate/100:0.0000} × {d.Days}/360");
		col.Item().Text($"Accrued Interest Due: {d.AccruedInterest:C}").Bold().FontSize(11);

		col.Item().PaddingTop(12).Text("Payment Instructions").Bold().FontColor(green);
		col.Item().Text($"Please wire {d.AccruedInterest:C} to {b.BankName}");
		col.Item().Text($"Routing: {p.Aba}   Account: {p.Account}");
		col.Item().Text($"Reference: {p.Reference}");

		col.Item().PaddingTop(20).Text("Sincerely,");
		col.Item().Text(b.BankName);
		col.Item().Text(b.Department);
	});
}

// ============================================================
// TEMPLATE 4 – Bold Formal (Black / Red accents)
// ============================================================
static void BuildTemplate4_BoldFormal(PageDescriptor page, AccrualData d, BankDetails b,
	(string Aba, string Account, string AccountName, string Swift, string Reference) p)
{
	page.Header().Column(c =>
	{
		c.Item().Text(b.BankName).Bold().FontSize(15);
		c.Item().Text(b.Department).FontSize(8);
		c.Item().LineHorizontal(3).LineColor("#8B0000");
	});

	page.Content().PaddingTop(12).Column(col =>
	{
		col.Item().Background("#8B0000").Padding(6).AlignCenter()
			.Text("OFFICIAL INTEREST ACCRUAL NOTICE").Bold().FontColor(Colors.White).FontSize(11);

		col.Item().PaddingTop(10).Text($"TO: {d.Borrower}").Bold().FontSize(10);
		col.Item().Text($"Facility Reference: {d.FacilityId}");
		col.Item().Text($"Payment Due: {d.PaymentDate:MMMM d, yyyy}").Bold();

		col.Item().PaddingTop(12).Table(t =>
		{
			t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
			t.Cell().Border(0.7f).Padding(5).Text("Interest Period");
			t.Cell().Border(0.7f).Padding(5).Text($"{d.PeriodStart:dd-MMM-yyyy} – {d.PeriodEnd:dd-MMM-yyyy}");
			t.Cell().Border(0.7f).Padding(5).Text("All-in Rate");
			t.Cell().Border(0.7f).Padding(5).Text($"{d.AllInRate:0.00}%");
			t.Cell().Border(0.7f).Padding(5).Text("Principal");
			t.Cell().Border(0.7f).Padding(5).Text(d.BeginningPrincipal.ToString("C"));
			t.Cell().Border(0.7f).Background("#FFF0F0").Padding(5).Text("Amount Due").Bold();
			t.Cell().Border(0.7f).Background("#FFF0F0").Padding(5).Text(d.AccruedInterest.ToString("C")).Bold();
		});

		col.Item().PaddingTop(14).Text("WIRE TRANSFER DETAILS").Bold();
		col.Item().Text($"{b.BankName}  |  ABA {p.Aba}  |  Acct {p.Account}");
		col.Item().Text($"Ref: {p.Reference}");

		col.Item().PaddingTop(18).Text("FOR THE ADMINISTRATIVE AGENT");
		col.Item().Text(b.BankName).Bold();
	});
}

// ============================================================
// TEMPLATE 5 – Elegant Serif / Traditional Letter
// ============================================================
static void BuildTemplate5_ElegantSerif(PageDescriptor page, AccrualData d, BankDetails b,
	(string Aba, string Account, string AccountName, string Swift, string Reference) p)
{
	page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

	page.Header().AlignCenter().Column(c =>
	{
		c.Item().Text(b.BankName).Bold().FontSize(14);
		c.Item().Text($"{b.AddressLine1}  •  {b.CityStateZip}");
		c.Item().Text($"Telephone {b.Telephone}");
		c.Item().PaddingTop(4).LineHorizontal(0.7f);
	});

	page.Content().PaddingTop(15).Column(col =>
	{
		col.Item().Text($"{DateTime.Today:MMMM d, yyyy}");
		col.Item().PaddingTop(12).Text(d.Borrower);
		col.Item().Text("Attn: Loan Administration");

		col.Item().PaddingTop(14).Text("Re: Notice of Interest Accrual").Bold().Underline();

		col.Item().PaddingTop(10).Text("Dear Sirs:");

		col.Item().PaddingTop(6).Text($"We write to advise you that interest has accrued on Facility {d.FacilityId} for the period from {d.PeriodStart:MMMM d, yyyy} to {d.PeriodEnd:MMMM d, yyyy} ({d.Days} days on an Actual/360 basis).");

		col.Item().PaddingTop(8).Text($"The applicable rate was {d.AllInRate:0.00}% ({d.BaseRate:0.00}% + {d.Margin:0.00}%). The outstanding principal was {d.BeginningPrincipal:C}, resulting in accrued interest of {d.AccruedInterest:C}.");

		col.Item().PaddingTop(8).Text($"Payment is due on {d.PaymentDate:MMMM d, yyyy}. Kindly arrange for the transfer of funds to the following account:");

		col.Item().PaddingTop(8).Text($"{b.BankName}");
		col.Item().Text($"ABA Number: {p.Aba}");
		col.Item().Text($"Account Number: {p.Account}");
		col.Item().Text($"Reference: {p.Reference}");

		col.Item().PaddingTop(16).Text("Yours faithfully,");
		col.Item().PaddingTop(24).Text(b.BankName);
		col.Item().Text("Administrative Agent");
	});
}

// ============================================================
// 4. Example Usage (run this in LINQPad)
// ============================================================

void Main()
{
	// Set inside Main rather than at file scope: LINQPad's Program kind wraps the query in a
	// class, and a bare assignment is not a valid class member.
	QuestPDF.Settings.License = LicenseType.Community;

	// Sample accrual (you will replace this with real data)
	var accrual = new AccrualData
	{
		FacilityId = "LN226909",
		Borrower = "ACCO Brands Corporation",
		PeriodStart = new DateTime(2006, 10, 26),
		PeriodEnd = new DateTime(2006, 11, 9),
		Days = 14,
		BaseRate = 5.37m,
		Margin = 1.75m,
		BeginningPrincipal = 106_000_000m,
		AccruedInterest = 293_502.22m,
		PaymentDate = new DateTime(2007, 1, 19)
	};

	// You feed these values
	var bank = new BankDetails
	{
		BankName = "Meridian Trust Agent Bank, N.A.",
		AddressLine1 = "One World Trade Center, Suite 4500",
		CityStateZip = "New York, NY 10007",
		Telephone = "(212) 555-0199",
		Email = "loanops@meridianagentbank.com"
	};

	// Generate one of the five templates (1–5)
	int templateToUse = 1;          // ← change this to 1, 2, 3, 4 or 5

	byte[] pdfBytes = GenerateNotice(accrual, bank, templateToUse);

	// Save to disk (LINQPad will also let you dump it)
	string path = Path.Combine(Path.GetTempPath(), $"Notice_Template{templateToUse}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
	File.WriteAllBytes(path, pdfBytes);

	path.Dump("PDF saved to");
}
