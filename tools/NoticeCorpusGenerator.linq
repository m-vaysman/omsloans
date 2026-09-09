<Query Kind="Program">
  <NuGetReference Version="2024.10.2">QuestPDF</NuGetReference>
  <Namespace>QuestPDF.Fluent</Namespace>
  <Namespace>QuestPDF.Helpers</Namespace>
  <Namespace>QuestPDF.Infrastructure</Namespace>
  <Namespace>System.Globalization</Namespace>
  <Namespace>System.Text.Json</Namespace>
  <Namespace>System.Text.Json.Nodes</Namespace>
</Query>

// LINQPad script - Notice corpus generator
//
// Generates mock agent-bank notices as PDFs, each paired with the extraction the notice
// should produce. The PDF and its expected JSON are built from the same NoticeSpec, so the
// ground truth is correct by construction rather than by somebody reading a PDF and typing
// out what they saw.
//
// That pairing is the whole point. A prompt edit can be replayed against the corpus and the
// provider's answer compared against a known-correct one, which is what makes "did this
// prompt get better or worse" a measurement instead of an impression.
//
// The expected JSON matches src/OmsLoan.Domain/Extractors/Prompts/extraction.v1.schema.json
// exactly, so the same ExtractedFieldFlattener flattens both halves of the comparison and a
// change to the path convention applies to both automatically.
//
// Nothing here is real. Borrowers, facilities, agent banks and payment details are invented,
// and every page carries a MOCK watermark.
//
// NuGet (QuestPDF) and namespaces are declared in the Query header above rather than with
// #r / using directives, which is how LINQPad resolves them.

// ============================================================
// 1. What a notice says
// ============================================================

/// <summary>The event types the extraction schema allows, in its own wire spelling.</summary>
public enum EventKind
{
	RateReset,
	InterestPayment,
	PrincipalPayment,
	Fee,
	Rollover,
}

/// <summary>
/// One economic event. Every property is nullable and <b>a null is not rendered</b>, which is
/// the mechanism that makes the corpus able to test "the notice did not state this".
/// </summary>
/// <remarks>
/// Leaving <see cref="AllInRate"/> null while setting <see cref="BaseRate"/> and
/// <see cref="Margin"/> produces a notice that states the two components and not the total.
/// The prompt says never compute; this is the fixture that catches a model that does.
/// </remarks>
public class NoticeEvent
{
	public EventKind Kind { get; set; }

	// Dates
	public DateTime? EffectiveDate { get; set; }
	public DateTime? PeriodStart { get; set; }
	public DateTime? PeriodEnd { get; set; }
	public DateTime? PaymentDueDate { get; set; }
	public DateTime? RateSetDate { get; set; }

	// Rates, held as printed percentages (5.37 means 5.37%). Normalised on the way out.
	public string BaseRateIndex { get; set; }
	public decimal? BaseRate { get; set; }
	public decimal? Margin { get; set; }
	public decimal? AllInRate { get; set; }
	public string DayCountConvention { get; set; }
	public int? DaysInPeriod { get; set; }

	// Amounts
	public decimal? OutstandingPrincipalBefore { get; set; }
	public decimal? PrincipalAmount { get; set; }
	public decimal? OutstandingPrincipalAfter { get; set; }
	public decimal? AccruedInterest { get; set; }
	public decimal? FeeAmount { get; set; }
	public string FeeType { get; set; }
	public decimal? DrawdownAmount { get; set; }
	public decimal? CommitmentReductionAmount { get; set; }
	public decimal? UnfundedCommitment { get; set; }
	public decimal? LenderShareAmount { get; set; }
	public decimal? GlobalAmount { get; set; }
}

/// <summary>A whole notice: document-level identifiers and one or more events.</summary>
public class NoticeSpec
{
	public string Scenario { get; set; }

	// Identifiers. No agent bank: a bank routes the money, it does not describe the
	// economics, and the extraction schema has no field for one.
	public string BorrowerName { get; set; }
	public string FacilityName { get; set; }
	public string FacilityId { get; set; }
	public string TrancheName { get; set; }
	public string Cusip { get; set; }
	public string Lin { get; set; }
	public string Currency { get; set; } = "USD";

	public DateTime NoticeDate { get; set; }

	public List<NoticeEvent> Events { get; set; } = new List<NoticeEvent>();
}

/// <summary>
/// The agent bank's letterhead and wire details. Deliberately absent from the expected JSON.
/// </summary>
/// <remarks>
/// Every generated notice carries a full set of payment instructions on the page, because
/// the interesting test is adversarial: the data is right there in the document and the
/// extraction must still come back without it. A corpus of notices with no bank details in
/// them would prove nothing about a rule whose entire job is to refuse data that is present.
/// </remarks>
public class BankDetails
{
	public string BankName { get; set; }
	public string AddressLine1 { get; set; }
	public string CityStateZip { get; set; }
	public string Telephone { get; set; }
	public string Email { get; set; }
	public string Department { get; set; } = "Loan Operations / Agency Services";
}

public class PaymentInstructions
{
	public string Aba { get; set; }
	public string Account { get; set; }
	public string AccountName { get; set; }
	public string Swift { get; set; }
	public string Reference { get; set; }
}

// ============================================================
// 2. Formatting - printed form and normalised form
// ============================================================

/// <summary>
/// How a value appears on the page. The expected JSON holds the normalised form of the same
/// value, and the pair is what a comparison is actually testing: the model has to read
/// "5.37%" off a page and return 0.0537.
/// </summary>
public static class Printed
{
	static readonly Dictionary<string, string> Symbols = new Dictionary<string, string>
	{
		["USD"] = "$", ["EUR"] = "€", ["GBP"] = "£",
	};

	/// <summary>
	/// Money as an agent bank writes it. Not ToString("C") — that formats to whatever culture
	/// the machine running LINQPad happens to have, so a EUR facility printed dollar signs and
	/// the corpus silently depended on the developer's regional settings.
	/// </summary>
	public static string Money(decimal amount, string currency)
	{
		string symbol;
		if (!Symbols.TryGetValue(currency ?? "USD", out symbol)) symbol = currency + " ";

		return symbol + amount.ToString("N2", CultureInfo.InvariantCulture);
	}

	public static string Rate(decimal percent) =>
		percent.ToString("0.0000", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.') + "%";

	public static string Date(DateTime value) => value.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);

	public static string ShortDate(DateTime value) => value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture);

	public static string Title(EventKind kind)
	{
		switch (kind)
		{
			case EventKind.RateReset: return "INTEREST RATE RESET";
			case EventKind.InterestPayment: return "INTEREST PAYMENT";
			case EventKind.PrincipalPayment: return "PRINCIPAL PAYMENT";
			case EventKind.Fee: return "FEE";
			case EventKind.Rollover: return "ROLLOVER";
			default: return "NOTICE";
		}
	}
}

/// <summary>The wire spellings the extraction schema uses. Must match NoticeTypes.WireName().</summary>
public static class Wire
{
	public static string Name(EventKind kind)
	{
		switch (kind)
		{
			case EventKind.RateReset: return "rate_reset";
			case EventKind.InterestPayment: return "interest_payment";
			case EventKind.PrincipalPayment: return "principal_payment";
			case EventKind.Fee: return "fee";
			case EventKind.Rollover: return "rollover";
			default: return "unknown";
		}
	}
}

// ============================================================
// 3. The label/value lines a template renders
// ============================================================

/// <summary>
/// Everything an event states, as label/value pairs, skipping whatever is null.
/// </summary>
/// <remarks>
/// This is the single place that decides what reaches the page, and every template renders
/// the same lines in its own styling. That is what keeps the PDF and the expected JSON in
/// agreement: both are projections of the same spec, so a template cannot state a field the
/// ground truth does not claim, or omit one it does.
///
/// The previous generator hardcoded an interest-accrual table into each template, and the
/// five of them had drifted to stating different subsets of the data — one had no base rate
/// or margin at all. Ground truth would have had to be computed per template, from a
/// hand-maintained table that nothing would have kept honest across a restyling.
/// </remarks>
static List<KeyValuePair<string, string>> Lines(NoticeEvent e, string currency)
{
	var lines = new List<KeyValuePair<string, string>>();

	void Add(string label, string value)
	{
		if (!string.IsNullOrEmpty(value)) lines.Add(new KeyValuePair<string, string>(label, value));
	}

	void Date(string label, DateTime? value) { if (value.HasValue) Add(label, Printed.Date(value.Value)); }
	void Money(string label, decimal? value) { if (value.HasValue) Add(label, Printed.Money(value.Value, currency)); }
	void Rate(string label, decimal? value) { if (value.HasValue) Add(label, Printed.Rate(value.Value)); }

	if (e.PeriodStart.HasValue && e.PeriodEnd.HasValue)
		Add("Interest Period", Printed.Date(e.PeriodStart.Value) + " to " + Printed.Date(e.PeriodEnd.Value));
	else
	{
		Date("Period Start", e.PeriodStart);
		Date("Period End", e.PeriodEnd);
	}

	Date("Rate Set Date", e.RateSetDate);
	Date("Effective Date", e.EffectiveDate);
	Date("Payment Due Date", e.PaymentDueDate);

	Add("Base Rate Index", e.BaseRateIndex);
	Rate("Base Rate", e.BaseRate);
	Rate("Applicable Margin", e.Margin);
	Rate("All-in Rate", e.AllInRate);
	Add("Day Count", e.DayCountConvention);
	if (e.DaysInPeriod.HasValue) Add("Days in Period", e.DaysInPeriod.Value.ToString(CultureInfo.InvariantCulture));

	Money("Outstanding Principal (before)", e.OutstandingPrincipalBefore);
	Money("Principal Amount", e.PrincipalAmount);
	Money("Outstanding Principal (after)", e.OutstandingPrincipalAfter);
	Money("Accrued Interest", e.AccruedInterest);
	Add("Fee Type", e.FeeType);
	Money("Fee Amount", e.FeeAmount);
	Money("Drawdown Amount", e.DrawdownAmount);
	Money("Commitment Reduction", e.CommitmentReductionAmount);
	Money("Unfunded Commitment", e.UnfundedCommitment);
	Money("Lender Share", e.LenderShareAmount);
	Money("Global Amount", e.GlobalAmount);

	return lines;
}

/// <summary>The identifier lines, shared by every template for the same reason.</summary>
static List<KeyValuePair<string, string>> IdentifierLines(NoticeSpec s)
{
	var lines = new List<KeyValuePair<string, string>>();

	void Add(string label, string value)
	{
		if (!string.IsNullOrEmpty(value)) lines.Add(new KeyValuePair<string, string>(label, value));
	}

	Add("Borrower", s.BorrowerName);
	Add("Facility", s.FacilityName);
	Add("Facility ID", s.FacilityId);
	Add("Tranche", s.TrancheName);
	Add("CUSIP", s.Cusip);
	Add("LIN", s.Lin);
	Add("Currency", s.Currency);

	return lines;
}

// ============================================================
// 4. Ground truth
// ============================================================

/// <summary>
/// The extraction the notice should produce, in the shape of extraction.v1.schema.json.
/// </summary>
/// <remarks>
/// Three rules from the prompt are applied here, because the expected answer has to obey the
/// same ones the model is given:
///
/// - Rates are decimals. 5.37% on the page is 0.0537 here.
/// - Dates are ISO. "November 9, 2026" on the page is "2026-11-09" here.
/// - Amounts are plain numbers, with no symbol and no thousands separator.
///
/// And one rule that is enforced by omission: there is no payment_instructions object and no
/// bank name anywhere in the output, however much of it is printed on the page.
///
/// field_confidence is left empty. Ground truth has no confidence to state — the model's
/// confidence is one of the things being measured against it, not part of the answer.
/// </remarks>
static string ExpectedJson(NoticeSpec s)
{
	JsonNode Rate(decimal? percent) => percent.HasValue ? JsonValue.Create(percent.Value / 100m) : null;
	JsonNode Money(decimal? amount) => amount.HasValue ? JsonValue.Create(amount.Value) : null;
	JsonNode Iso(DateTime? value) => value.HasValue
		? JsonValue.Create(value.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
		: null;
	JsonNode Text(string value) => string.IsNullOrEmpty(value) ? null : JsonValue.Create(value);
	JsonNode Count(int? value) => value.HasValue ? JsonValue.Create(value.Value) : null;

	var events = new JsonArray();

	foreach (var e in s.Events)
	{
		events.Add(new JsonObject
		{
			["type"] = Wire.Name(e.Kind),
			["dates"] = new JsonObject
			{
				["effective_date"] = Iso(e.EffectiveDate),
				["period_start"] = Iso(e.PeriodStart),
				["period_end"] = Iso(e.PeriodEnd),
				["payment_due_date"] = Iso(e.PaymentDueDate),
				["rate_set_date"] = Iso(e.RateSetDate),
			},
			["economics"] = new JsonObject
			{
				["base_rate_index"] = Text(e.BaseRateIndex),
				["base_rate"] = Rate(e.BaseRate),
				["margin"] = Rate(e.Margin),
				["all_in_rate"] = Rate(e.AllInRate),
				["day_count_convention"] = Text(e.DayCountConvention),
				["days_in_period"] = Count(e.DaysInPeriod),
				["outstanding_principal_before"] = Money(e.OutstandingPrincipalBefore),
				["principal_amount"] = Money(e.PrincipalAmount),
				["outstanding_principal_after"] = Money(e.OutstandingPrincipalAfter),
				["accrued_interest"] = Money(e.AccruedInterest),
				["fee_amount"] = Money(e.FeeAmount),
				["fee_type"] = Text(e.FeeType),
				["drawdown_amount"] = Money(e.DrawdownAmount),
				["commitment_reduction_amount"] = Money(e.CommitmentReductionAmount),
				["unfunded_commitment"] = Money(e.UnfundedCommitment),
				["lender_share_amount"] = Money(e.LenderShareAmount),
				["global_amount"] = Money(e.GlobalAmount),
			},
			["warnings"] = new JsonArray(),
		});
	}

	var root = new JsonObject
	{
		["identifiers"] = new JsonObject
		{
			["borrower_name"] = Text(s.BorrowerName),
			["facility_name"] = Text(s.FacilityName),
			["facility_id"] = Text(s.FacilityId),
			["tranche_name"] = Text(s.TrancheName),
			["cusip"] = Text(s.Cusip),
			["lin"] = Text(s.Lin),
			["currency"] = Text(s.Currency),
		},
		["notice_date"] = Iso(s.NoticeDate),
		["events"] = events,
		["field_confidence"] = new JsonObject(),
		["warnings"] = new JsonArray(),
	};

	return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
}

// ============================================================
// 5. Watermark
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
// 6. Generator entry point
// ============================================================

public static byte[] GenerateNotice(NoticeSpec spec, BankDetails bank, PaymentInstructions payment, int templateNumber)
{
	if (templateNumber < 1 || templateNumber > 5)
		throw new ArgumentOutOfRangeException("templateNumber", "Template must be 1-5");

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
				case 1: BuildTemplate1_ClassicBlue(page, spec, bank, payment); break;
				case 2: BuildTemplate2_ModernDark(page, spec, bank, payment); break;
				case 3: BuildTemplate3_ConservativeGreen(page, spec, bank, payment); break;
				case 4: BuildTemplate4_BoldFormal(page, spec, bank, payment); break;
				case 5: BuildTemplate5_ElegantSerif(page, spec, bank, payment); break;
			}
		});
	});

	// Set explicitly rather than left to the default. QuestPDF emits empty document
	// properties, which is harmless but means a generated notice carries no attribution at
	// all; naming Cobbler Hill LLC keeps the PDF consistent with the mock spreadsheet and
	// leaves no trace of whatever tooling happened to produce it.
	return document
		.WithMetadata(new DocumentMetadata
		{
			Title = "Loan Notice - " + spec.FacilityId,
			Author = "Cobbler Hill LLC",
			Creator = "Cobbler Hill LLC",
			Producer = "Cobbler Hill LLC",
			Subject = "Mock loan notice - not a real instrument",
		})
		.GeneratePdf();
}

// ============================================================
// TEMPLATE 1 - Classic Corporate Blue
// ============================================================
static void BuildTemplate1_ClassicBlue(PageDescriptor page, NoticeSpec s, BankDetails b, PaymentInstructions p)
{
	var blue = "#1F4E79";
	var lightBlue = "#D6E3F0";

	page.Header().Column(col =>
	{
		col.Item().AlignCenter().Text(b.BankName).Bold().FontSize(16).FontColor(blue);
		col.Item().AlignCenter().Text(b.Department).FontSize(8).FontColor("#555555");
		col.Item().AlignCenter().Text(b.AddressLine1 + "  •  " + b.CityStateZip).FontSize(8);
		col.Item().AlignCenter().Text("Tel: " + b.Telephone + "  •  " + b.Email).FontSize(8);
		col.Item().PaddingTop(4).LineHorizontal(2).LineColor(blue);
	});

	page.Content().PaddingTop(12).Column(col =>
	{
		col.Item().AlignCenter().Text(HeadlineFor(s)).Bold().FontSize(12).FontColor(blue);
		col.Item().AlignCenter().Text("CONFIDENTIAL — FOR ADDRESSEE ONLY").FontSize(8).FontColor("#A00").Bold();
		col.Item().PaddingVertical(8);

		col.Item().Background(lightBlue).Padding(6).Row(r =>
		{
			r.RelativeItem().Text("Date of Notice: " + Printed.Date(s.NoticeDate));
			r.RelativeItem().AlignRight().Text("Facility ID: " + s.FacilityId);
		});

		col.Item().PaddingTop(10).Text("TO: " + s.BorrowerName).Bold();
		col.Item().Text("Attention: Treasury / Loan Administration");

		col.Item().PaddingTop(8).Table(t =>
		{
			t.ColumnsDefinition(c => { c.RelativeColumn(2.2f); c.RelativeColumn(1.3f); });
			foreach (var line in IdentifierLines(s))
			{
				t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Padding(3).Text(line.Key);
				t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Padding(3).AlignRight().Text(line.Value);
			}
		});

		col.Item().PaddingTop(10).Text("Ladies and Gentlemen:");
		col.Item().Text("Pursuant to the Credit Agreement, we hereby notify you of the following.");

		foreach (var e in s.Events)
		{
			col.Item().PaddingTop(12).Text(Printed.Title(e.Kind)).Bold().FontColor(blue);
			col.Item().PaddingTop(4).Table(t =>
			{
				t.ColumnsDefinition(c => { c.RelativeColumn(2.2f); c.RelativeColumn(1.3f); });
				t.Header(h =>
				{
					h.Cell().Background(blue).Padding(4).Text("Item").FontColor(Colors.White).Bold();
					h.Cell().Background(blue).Padding(4).AlignRight().Text("Detail").FontColor(Colors.White).Bold();
				});

				foreach (var line in Lines(e, s.Currency))
				{
					// Typed explicitly: in QuestPDF 2024.10 Colors.White is a Color, and string
					// and Color convert to each other, so `var` leaves the ternary with no
					// natural type.
					Color bg = Colors.White;
					t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Background(bg).Padding(4).Text(line.Key);
					t.Cell().BorderBottom(0.5f).BorderColor("#CCC").Background(bg).Padding(4).AlignRight().Text(line.Value).Bold();
				}
			});
		}

		col.Item().PaddingTop(14).Text("PAYMENT INSTRUCTIONS").Bold().FontColor(blue);
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
	});

	page.Footer().AlignCenter().Text("This is a MOCK notice generated for demonstration purposes only.").FontSize(7).FontColor("#888");
}

// ============================================================
// TEMPLATE 2 - Modern Dark Header
// ============================================================
static void BuildTemplate2_ModernDark(PageDescriptor page, NoticeSpec s, BankDetails b, PaymentInstructions p)
{
	page.Header().Background("#1A1A2E").Padding(12).Column(c =>
	{
		c.Item().Text(b.BankName).FontSize(18).Bold().FontColor(Colors.White);
		c.Item().Text(b.Department + "  |  " + b.Telephone + "  |  " + b.Email).FontSize(8).FontColor("#AAAAAA");
	});

	page.Content().PaddingTop(15).Column(col =>
	{
		col.Item().Text(HeadlineFor(s)).Bold().FontSize(14);
		col.Item().Text("Facility " + s.FacilityId + "  •  " + s.BorrowerName).FontSize(9).FontColor("#555");
		col.Item().Text("Notice Date " + Printed.Date(s.NoticeDate)).FontSize(9).FontColor("#555");

		col.Item().PaddingTop(12).Column(c =>
		{
			foreach (var line in IdentifierLines(s))
				c.Item().Text(line.Key + ": " + line.Value).FontSize(9);
		});

		foreach (var e in s.Events)
		{
			col.Item().PaddingTop(15).LineHorizontal(1).LineColor("#EEE");
			col.Item().PaddingTop(10).Text(Printed.Title(e.Kind)).Bold();

			col.Item().PaddingTop(4).Column(c =>
			{
				foreach (var line in Lines(e, s.Currency))
					c.Item().Row(r =>
					{
						r.RelativeItem(2).Text(line.Key).FontColor("#888");
						r.RelativeItem(2).AlignRight().Text(line.Value).Bold();
					});
			});
		}

		col.Item().PaddingTop(14).Background("#F8F9FA").Padding(10).Column(c =>
		{
			c.Item().Text("WIRE INSTRUCTIONS").Bold().FontSize(9);
			c.Item().Text(b.BankName);
			c.Item().Text("ABA " + p.Aba + "  •  Acct " + p.Account + "  •  SWIFT " + p.Swift);
			c.Item().Text("Ref: " + p.Reference);
		});

		col.Item().PaddingTop(20).Text("Regards,");
		col.Item().Text(b.BankName).Bold();
	});
}

// ============================================================
// TEMPLATE 3 - Conservative Green
// ============================================================
static void BuildTemplate3_ConservativeGreen(PageDescriptor page, NoticeSpec s, BankDetails b, PaymentInstructions p)
{
	var green = "#1B4F3C";

	page.Header().Column(c =>
	{
		c.Item().AlignCenter().Text(b.BankName.ToUpperInvariant()).Bold().FontSize(13).FontColor(green);
		c.Item().AlignCenter().Text(b.AddressLine1 + ", " + b.CityStateZip).FontSize(8);
		c.Item().PaddingTop(3).LineHorizontal(1.5f).LineColor(green);
	});

	page.Content().PaddingTop(10).Column(col =>
	{
		col.Item().AlignCenter().Text(HeadlineFor(s)).Bold().FontSize(11).FontColor(green);
		col.Item().PaddingTop(8);

		col.Item().Text("Date of Notice: " + Printed.Date(s.NoticeDate));
		foreach (var line in IdentifierLines(s))
			col.Item().Text(line.Key + ": " + line.Value);

		foreach (var e in s.Events)
		{
			col.Item().PaddingTop(12).Text(Printed.Title(e.Kind)).Bold().FontColor(green);
			foreach (var line in Lines(e, s.Currency))
				col.Item().Text(line.Key + ": " + line.Value);
		}

		col.Item().PaddingTop(12).Text("Payment Instructions").Bold().FontColor(green);
		col.Item().Text("Please remit to " + b.BankName);
		col.Item().Text("Routing: " + p.Aba + "   Account: " + p.Account + "   SWIFT: " + p.Swift);
		col.Item().Text("Reference: " + p.Reference);

		col.Item().PaddingTop(20).Text("Sincerely,");
		col.Item().Text(b.BankName);
		col.Item().Text(b.Department);
	});
}

// ============================================================
// TEMPLATE 4 - Bold Formal (Black / Red accents)
// ============================================================
static void BuildTemplate4_BoldFormal(PageDescriptor page, NoticeSpec s, BankDetails b, PaymentInstructions p)
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
			.Text("OFFICIAL " + HeadlineFor(s)).Bold().FontColor(Colors.White).FontSize(11);

		col.Item().PaddingTop(10).Text("TO: " + s.BorrowerName).Bold().FontSize(10);
		col.Item().Text("Date of Notice: " + Printed.Date(s.NoticeDate));

		col.Item().PaddingTop(8).Table(t =>
		{
			t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
			foreach (var line in IdentifierLines(s))
			{
				t.Cell().Border(0.7f).Padding(4).Text(line.Key);
				t.Cell().Border(0.7f).Padding(4).Text(line.Value);
			}
		});

		foreach (var e in s.Events)
		{
			col.Item().PaddingTop(12).Text(Printed.Title(e.Kind)).Bold();
			col.Item().PaddingTop(4).Table(t =>
			{
				t.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
				foreach (var line in Lines(e, s.Currency))
				{
					t.Cell().Border(0.7f).Padding(5).Text(line.Key);
					t.Cell().Border(0.7f).Padding(5).Text(line.Value);
				}
			});
		}

		col.Item().PaddingTop(14).Text("WIRE TRANSFER DETAILS").Bold();
		col.Item().Text(b.BankName + "  |  ABA " + p.Aba + "  |  Acct " + p.Account);
		col.Item().Text("SWIFT " + p.Swift + "  |  Ref: " + p.Reference);

		col.Item().PaddingTop(18).Text("FOR THE ADMINISTRATIVE AGENT");
		col.Item().Text(b.BankName).Bold();
	});
}

// ============================================================
// TEMPLATE 5 - Elegant Serif / Traditional Letter
// ============================================================
static void BuildTemplate5_ElegantSerif(PageDescriptor page, NoticeSpec s, BankDetails b, PaymentInstructions p)
{
	page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(10));

	page.Header().AlignCenter().Column(c =>
	{
		c.Item().Text(b.BankName).Bold().FontSize(14);
		c.Item().Text(b.AddressLine1 + "  •  " + b.CityStateZip);
		c.Item().Text("Telephone " + b.Telephone);
		c.Item().PaddingTop(4).LineHorizontal(0.7f);
	});

	page.Content().PaddingTop(15).Column(col =>
	{
		col.Item().Text(Printed.Date(s.NoticeDate));
		col.Item().PaddingTop(12).Text(s.BorrowerName);
		col.Item().Text("Attn: Loan Administration");

		col.Item().PaddingTop(14).Text("Re: " + HeadlineFor(s)).Bold().Underline();

		col.Item().PaddingTop(10).Text("Dear Sirs:");
		col.Item().PaddingTop(6).Text("We write to advise you of the following in respect of the facility identified below.");

		col.Item().PaddingTop(8).Column(c =>
		{
			foreach (var line in IdentifierLines(s))
				c.Item().Text(line.Key + ": " + line.Value);
		});

		foreach (var e in s.Events)
		{
			col.Item().PaddingTop(10).Text(Printed.Title(e.Kind)).Bold();
			foreach (var line in Lines(e, s.Currency))
				col.Item().Text("    " + line.Key + ": " + line.Value);
		}

		col.Item().PaddingTop(10).Text("Kindly arrange for the transfer of funds to the following account:");
		col.Item().PaddingTop(6).Text(b.BankName);
		col.Item().Text("ABA Number: " + p.Aba);
		col.Item().Text("Account Number: " + p.Account);
		col.Item().Text("SWIFT: " + p.Swift);
		col.Item().Text("Reference: " + p.Reference);

		col.Item().PaddingTop(16).Text("Yours faithfully,");
		col.Item().PaddingTop(24).Text(b.BankName);
		col.Item().Text("Administrative Agent");
	});
}

/// <summary>
/// The document headline. A notice stating two events gets a combined one, because a document
/// titled only "RATE RESET" that also contains a paydown is the exact trap the events array
/// exists to handle — and it is worth having in the corpus rather than avoiding.
/// </summary>
static string HeadlineFor(NoticeSpec s)
{
	if (s.Events.Count == 0) return "LOAN NOTICE";
	if (s.Events.Count == 1) return "NOTICE OF " + Printed.Title(s.Events[0].Kind);

	var kinds = s.Events.Select(e => Printed.Title(e.Kind)).Distinct().ToArray();
	return "NOTICE OF " + string.Join(" AND ", kinds);
}

// ============================================================
// 7. The roster - names, banks, indices
// ============================================================

static readonly string[] Borrowers =
{
	"Northstar Packaging Inc", "ACCO Brands Corporation", "Calder Aerospace Holdings LLC",
	"Redwood Vale Nutrition Corp", "Harbour Point Logistics Ltd", "Ferrier Industrial Group",
	"Bluestem Health Partners LP", "Ashcroft Materials Company", "Vantage Rail Services Inc",
	"Pemberton Specialty Chemicals", "Alderwood Media Holdings", "Kestrel Marine Terminals LLC",
};

static readonly string[] FacilityNames =
{
	"Senior Secured Term Loan B", "Revolving Credit Facility", "Delayed Draw Term Loan",
	"Second Lien Term Facility", "Multicurrency Revolving Facility", "Incremental Term Loan A",
};

/// <summary>
/// Tranche names that go with a facility, rather than drawn at random from all of them.
/// </summary>
/// <remarks>
/// A revolver with a "Term B-1" tranche is not a document any agent bank would send, and an
/// incoherent notice is a bad fixture: a model that reads it badly may be reading the
/// nonsense correctly, so a failure no longer tells you anything about the prompt. Every
/// notice in the corpus should be one a loan operations team could plausibly receive.
/// </remarks>
static string[] TranchesFor(string facilityName)
{
	if (facilityName.IndexOf("Revolv", StringComparison.OrdinalIgnoreCase) >= 0)
		return new[] { "Revolver A", "Revolver B", null };

	if (facilityName.IndexOf("Delayed Draw", StringComparison.OrdinalIgnoreCase) >= 0)
		return new[] { "DDTL-1", "DDTL-2", null };

	if (facilityName.IndexOf("Term Loan A", StringComparison.OrdinalIgnoreCase) >= 0
		|| facilityName.IndexOf("Term Loan", StringComparison.OrdinalIgnoreCase) >= 0)
		return new[] { "Term B-1", "Term B-2", "Term A", null };

	return new[] { "Term B-1", null };
}

/// <summary>
/// The benchmark a currency actually resets against, with the day count that goes with it.
/// </summary>
/// <remarks>
/// SONIA on a USD facility, or SONIA on Actual/360, is the same problem as the tranche: it
/// makes the fixture unrealistic in a way that muddies what a failure means. Sterling resets
/// against SONIA on Actual/365; SOFR and EURIBOR are Actual/360.
/// </remarks>
static KeyValuePair<string, string> IndexFor(Random rng, string currency)
{
	switch (currency)
	{
		case "GBP":
			return new KeyValuePair<string, string>("SONIA", "Actual/365");
		case "EUR":
			return new KeyValuePair<string, string>(Pick(rng, new[] { "EURIBOR", "EURIBOR", "ESTR" }), "Actual/360");
		default:
			return new KeyValuePair<string, string>(
				Pick(rng, new[] { "Term SOFR", "Term SOFR", "Daily Simple SOFR", "Prime Rate" }),
				Pick(rng, new[] { "Actual/360", "Actual/360", "30/360" }));
	}
}

static readonly string[] AgentBanks =
{
	"Meridian Trust Agent Bank, N.A.", "Coastal Fidelity Bank", "Grantham & Pierce Trust Company",
	"Union Sable Bank, N.A.", "Halverson Agency Services", "Britannia Loan Trust plc",
};

static readonly string[] Cities =
{
	"New York, NY 10007", "Charlotte, NC 28202", "Chicago, IL 60606",
	"London EC2V 7NQ", "Boston, MA 02110", "Dallas, TX 75201",
};

static readonly string[] FeeTypes = { "Commitment Fee", "Amendment Fee", "Administrative Agency Fee", "Upfront Fee" };

// ============================================================
// 8. Scenario builders
// ============================================================

/// <summary>
/// Builds the corpus. Seeded, so the same run produces the same notices — a corpus that
/// reshuffles itself every run cannot be used to compare one prompt version against another,
/// which is the only reason it exists.
/// </summary>
static List<NoticeSpec> BuildCorpus(int seed, int notices)
{
	var rng = new Random(seed);
	var specs = new List<NoticeSpec>();

	// Every scenario appears before any repeats, so a small corpus still covers all of them.
	var scenarios = new Func<Random, NoticeSpec>[]
	{
		InterestPayment,
		RateReset,
		RateResetWithoutAllInRate,
		PrincipalPayment,
		CombinedPaydownAndRateReset,
		Fee,
		Rollover,
		RevolverDrawAndCommitmentChange,
	};

	for (int i = 0; i < notices; i++)
		specs.Add(scenarios[i % scenarios.Length](rng));

	return specs;
}

static T Pick<T>(Random rng, T[] values) => values[rng.Next(values.Length)];

/// <summary>Document-level identifiers, varied per notice. The agent bank is chosen separately.</summary>
static NoticeSpec NewSpec(Random rng, string scenario)
{
	var currency = Pick(rng, new[] { "USD", "USD", "USD", "EUR", "GBP" });
	var facilityName = Pick(rng, FacilityNames);

	return new NoticeSpec
	{
		Scenario = scenario,
		BorrowerName = Pick(rng, Borrowers),
		FacilityName = facilityName,
		FacilityId = "LN" + rng.Next(100000, 999999).ToString(CultureInfo.InvariantCulture),
		TrancheName = Pick(rng, TranchesFor(facilityName)),
		Cusip = rng.Next(2) == 0 ? null : RandomAlphanumeric(rng, 9),
		Lin = rng.Next(2) == 0 ? null : "LX" + rng.Next(1000000, 9999999).ToString(CultureInfo.InvariantCulture),
		Currency = currency,
		NoticeDate = new DateTime(2026, 1, 1).AddDays(rng.Next(0, 330)),
	};
}

static string RandomAlphanumeric(Random rng, int length)
{
	const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ0123456789";
	var chars = new char[length];
	for (int i = 0; i < length; i++) chars[i] = alphabet[rng.Next(alphabet.Length)];
	return new string(chars);
}

/// <summary>Rounded to a cent, because that is how it appears on the page and in the answer.</summary>
static decimal Interest(decimal principal, decimal allInRatePercent, int days, string dayCount)
{
	decimal basis = dayCount == "30/360" || dayCount == "Actual/360" ? 360m : 365m;
	return Math.Round(principal * (allInRatePercent / 100m) * days / basis, 2);
}

static NoticeSpec InterestPayment(Random rng)
{
	var s = NewSpec(rng, "interest-payment");
	var start = s.NoticeDate.AddDays(-rng.Next(28, 92));
	var end = s.NoticeDate.AddDays(rng.Next(1, 20));
	var days = (int)(end - start).TotalDays;
	var benchmark = IndexFor(rng, s.Currency);
	var dayCount = benchmark.Value;
	var baseRate = Math.Round((decimal)(rng.NextDouble() * 4 + 2), 4);
	var margin = Math.Round((decimal)(rng.NextDouble() * 3 + 1), 4);
	var principal = rng.Next(10, 200) * 1_000_000m;

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.InterestPayment,
		PeriodStart = start,
		PeriodEnd = end,
		PaymentDueDate = end.AddDays(2),
		BaseRateIndex = benchmark.Key,
		BaseRate = baseRate,
		Margin = margin,
		AllInRate = baseRate + margin,
		DayCountConvention = dayCount,
		DaysInPeriod = days,
		OutstandingPrincipalBefore = principal,
		AccruedInterest = Interest(principal, baseRate + margin, days, dayCount),
	});

	return s;
}

static NoticeSpec RateReset(Random rng)
{
	var s = NewSpec(rng, "rate-reset");
	var benchmark = IndexFor(rng, s.Currency);
	var baseRate = Math.Round((decimal)(rng.NextDouble() * 4 + 2), 4);
	var margin = Math.Round((decimal)(rng.NextDouble() * 3 + 1), 4);
	var effective = s.NoticeDate.AddDays(rng.Next(2, 15));

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.RateReset,
		RateSetDate = s.NoticeDate,
		EffectiveDate = effective,
		PeriodStart = effective,
		PeriodEnd = effective.AddDays(rng.Next(28, 92)),
		BaseRateIndex = benchmark.Key,
		BaseRate = baseRate,
		Margin = margin,
		AllInRate = baseRate + margin,
		DayCountConvention = benchmark.Value,
	});

	return s;
}

/// <summary>
/// The never-compute fixture. The notice states a base rate and a margin and does not state
/// the total, so the correct answer has all_in_rate null. A model that adds the two and
/// returns a number is wrong, and this is the only kind of notice that catches it.
/// </summary>
static NoticeSpec RateResetWithoutAllInRate(Random rng)
{
	var s = RateReset(rng);
	s.Scenario = "rate-reset-no-all-in";
	s.Events[0].AllInRate = null;
	return s;
}

static NoticeSpec PrincipalPayment(Random rng)
{
	var s = NewSpec(rng, "principal-payment");
	var before = rng.Next(20, 200) * 1_000_000m;
	var paydown = Math.Round(before * (decimal)(rng.NextDouble() * 0.15 + 0.01), 2);
	var due = s.NoticeDate.AddDays(rng.Next(5, 30));

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.PrincipalPayment,
		EffectiveDate = due,
		PaymentDueDate = due,
		OutstandingPrincipalBefore = before,
		PrincipalAmount = paydown,
		OutstandingPrincipalAfter = before - paydown,
	});

	return s;
}

/// <summary>
/// Two events in one document, which is the case a prompt-per-notice-type could not express.
/// The paydown carries the principal and the reset carries the rates, and neither carries the
/// other's figures — a model that merges them into one event is wrong in a way this fixture
/// makes visible.
/// </summary>
static NoticeSpec CombinedPaydownAndRateReset(Random rng)
{
	var s = NewSpec(rng, "combined-paydown-and-rate-reset");
	var benchmark = IndexFor(rng, s.Currency);
	var before = rng.Next(20, 200) * 1_000_000m;
	var paydown = Math.Round(before * (decimal)(rng.NextDouble() * 0.12 + 0.01), 2);
	var effective = s.NoticeDate.AddDays(rng.Next(5, 25));
	var baseRate = Math.Round((decimal)(rng.NextDouble() * 4 + 2), 4);
	var margin = Math.Round((decimal)(rng.NextDouble() * 3 + 1), 4);

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.PrincipalPayment,
		EffectiveDate = effective,
		PaymentDueDate = effective,
		OutstandingPrincipalBefore = before,
		PrincipalAmount = paydown,
		OutstandingPrincipalAfter = before - paydown,
	});

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.RateReset,
		RateSetDate = s.NoticeDate,
		EffectiveDate = effective,
		PeriodStart = effective,
		PeriodEnd = effective.AddDays(rng.Next(28, 92)),
		BaseRateIndex = benchmark.Key,
		BaseRate = baseRate,
		Margin = margin,
		AllInRate = baseRate + margin,
		DayCountConvention = benchmark.Value,
	});

	return s;
}

static NoticeSpec Fee(Random rng)
{
	var s = NewSpec(rng, "fee");
	var due = s.NoticeDate.AddDays(rng.Next(5, 30));

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.Fee,
		EffectiveDate = due,
		PaymentDueDate = due,
		FeeType = Pick(rng, FeeTypes),
		FeeAmount = Math.Round((decimal)(rng.NextDouble() * 400000 + 5000), 2),
		UnfundedCommitment = rng.Next(2) == 0 ? (decimal?)null : rng.Next(5, 80) * 1_000_000m,
	});

	return s;
}

static NoticeSpec Rollover(Random rng)
{
	var s = NewSpec(rng, "rollover");
	var benchmark = IndexFor(rng, s.Currency);
	var effective = s.NoticeDate.AddDays(rng.Next(2, 12));
	var baseRate = Math.Round((decimal)(rng.NextDouble() * 4 + 2), 4);
	var margin = Math.Round((decimal)(rng.NextDouble() * 3 + 1), 4);

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.Rollover,
		EffectiveDate = effective,
		PeriodStart = effective,
		PeriodEnd = effective.AddDays(rng.Next(28, 183)),
		BaseRateIndex = benchmark.Key,
		BaseRate = baseRate,
		Margin = margin,
		AllInRate = baseRate + margin,
		DayCountConvention = benchmark.Value,
		GlobalAmount = rng.Next(10, 150) * 1_000_000m,
	});

	return s;
}

/// <summary>A revolver notice: a drawdown alongside a commitment reduction.</summary>
static NoticeSpec RevolverDrawAndCommitmentChange(Random rng)
{
	var s = NewSpec(rng, "revolver-draw-and-commitment-change");
	s.FacilityName = "Revolving Credit Facility";

	// Re-picked, because NewSpec chose a tranche to go with whatever facility it drew and
	// this scenario has just overridden that facility.
	s.TrancheName = Pick(rng, TranchesFor(s.FacilityName));

	var effective = s.NoticeDate.AddDays(rng.Next(1, 10));
	var commitment = rng.Next(50, 300) * 1_000_000m;
	var draw = Math.Round(commitment * (decimal)(rng.NextDouble() * 0.3 + 0.05), 2);
	var reduction = rng.Next(1, 20) * 1_000_000m;

	s.Events.Add(new NoticeEvent
	{
		Kind = EventKind.PrincipalPayment,
		EffectiveDate = effective,
		DrawdownAmount = draw,
		CommitmentReductionAmount = reduction,
		UnfundedCommitment = commitment - draw - reduction,
		LenderShareAmount = Math.Round(draw * 0.125m, 2),
		GlobalAmount = draw,
	});

	return s;
}

// ============================================================
// 9. Run it
// ============================================================

void Main()
{
	// Set inside Main rather than at file scope: LINQPad's Program kind wraps the query in a
	// class, and a bare assignment is not a valid class member.
	QuestPDF.Settings.License = LicenseType.Community;

	// ---- knobs -------------------------------------------------------------------------
	const int Seed = 20260909;      // change for a different corpus; keep for a reproducible one
	const int NoticeCount = 40;     // 8 scenarios, so 40 gives five of each
	string outputRoot = Path.Combine(
		Path.GetDirectoryName(Util.CurrentQueryPath), "..", "scripts", "Notices", "corpus");
	// ------------------------------------------------------------------------------------

	outputRoot = Path.GetFullPath(outputRoot);
	Directory.CreateDirectory(outputRoot);

	var rng = new Random(Seed);
	var specs = BuildCorpus(Seed, NoticeCount);
	var manifest = new JsonArray();

	for (int i = 0; i < specs.Count; i++)
	{
		var spec = specs[i];

		// Templates cycle rather than being drawn at random, so every scenario is seen in
		// every layout across a full corpus. Layout is the variable these are here to test.
		int template = (i % 5) + 1;

		var bank = new BankDetails
		{
			BankName = Pick(rng, AgentBanks),
			AddressLine1 = rng.Next(100, 999) + " " + Pick(rng, new[] { "Bishopsgate", "Park Avenue", "LaSalle Street", "Tryon Street", "Federal Street" }),
			CityStateZip = Pick(rng, Cities),
			Telephone = "(" + rng.Next(200, 989) + ") 555-" + rng.Next(1000, 9999).ToString("0000"),
			Email = "loanops@" + RandomAlphanumeric(rng, 6).ToLowerInvariant() + "bank.com",
		};

		var payment = new PaymentInstructions
		{
			// Nine digits, as a real routing number is. It is never extracted, but a fixture
			// that is obviously malformed is a weaker test of a rule that refuses well-formed
			// bank details.
			Aba = rng.Next(1000, 9999).ToString("0000") + rng.Next(10000, 99999).ToString("00000"),
			Account = rng.NextInt64(1000000000L, 9999999999L).ToString(CultureInfo.InvariantCulture),
			AccountName = spec.BorrowerName + " - Loan Collection Account",
			Swift = new string(Enumerable.Range(0, 4).Select(_ => (char)rng.Next('A', 'Z' + 1)).ToArray()) + "US33XXX",
			Reference = spec.FacilityId + " / " + spec.NoticeDate.ToString("MMM yyyy", CultureInfo.InvariantCulture),
		};

		var stem = (i + 1).ToString("000") + "-" + spec.Scenario + "-t" + template;
		var pdfPath = Path.Combine(outputRoot, stem + ".pdf");
		var expectedPath = Path.Combine(outputRoot, stem + ".expected.json");

		File.WriteAllBytes(pdfPath, GenerateNotice(spec, bank, payment, template));
		File.WriteAllText(expectedPath, ExpectedJson(spec));

		manifest.Add(new JsonObject
		{
			["pdf"] = stem + ".pdf",
			["expected"] = stem + ".expected.json",
			["scenario"] = spec.Scenario,
			["template"] = template,
			["events"] = spec.Events.Count,
			["currency"] = spec.Currency,
		});
	}

	// The manifest is what a regression harness reads: it does not have to guess file naming
	// or scan a directory, and it can report per-scenario rather than per-file.
	var manifestPath = Path.Combine(outputRoot, "manifest.json");
	File.WriteAllText(manifestPath, new JsonObject
	{
		["seed"] = Seed,
		["promptVersion"] = "extraction.v1",
		["generatedNotices"] = specs.Count,
		["notices"] = manifest,
	}.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

	(specs.Count + " notices written to " + outputRoot).Dump("Done");
	manifest.Dump("Corpus");
}
