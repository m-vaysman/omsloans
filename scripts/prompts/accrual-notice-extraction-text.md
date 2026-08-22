You are an expert at extracting structured data from bank Interest Accrual / Interest Payment Notices.

Below is the raw text extracted from a PDF notice. The document may use different wording for the same concepts (for example: "Base Rate", "BaseRate", "Reference Rate", "LIBOR" all refer to base_rate). Use semantic understanding rather than exact label matching.

Extract the following information and return **only** valid JSON with these exact keys:

{
  "agent_bank_name": "",
  "borrower_name": "",
  "facility_id": "",
  "notice_date": "",
  "interest_period_start": "",
  "interest_period_end": "",
  "days_in_period": null,
  "day_count_convention": "",
  "base_rate": null,
  "margin": null,
  "all_in_rate": null,
  "outstanding_principal": null,
  "accrued_interest": null,
  "payment_due_date": "",
  "payment_instructions": {
    "bank_name": "",
    "aba_routing_number": "",
    "account_number": "",
    "account_name": "",
    "swift_code": "",
    "reference": ""
  },
  "activity_type": "",
  "confidence_notes": ""
}

Guidelines:
- base_rate = the floating/reference rate (LIBOR, SOFR, Base Rate, Reference Rate, etc.)
- margin = the spread added to the base rate
- all_in_rate = base + margin (sometimes labeled "Interest Rate", "Applicable Rate", or "All-in Rate")
- accrued_interest = the final interest amount due (may appear as "Accrued Interest", "Accrual", "Interest Due", "Amount Due", etc.)
- Return pure numbers for rates and money fields (no %, $, or commas)
- Dates in YYYY-MM-DD format when possible
- If a value is missing or unclear, use null and note it in "confidence_notes"
- Do not invent or guess values

Here is the extracted text from the PDF:

---
[PASTE THE EXTRACTED PDF TEXT HERE]
---
