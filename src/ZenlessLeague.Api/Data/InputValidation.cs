namespace ZenlessLeague.Api.Data;

// Boundary checks for free-text request fields, so blank or over-long input is a clear 400 instead
// of reaching the database (where it either gets stored as-is or trips a column length limit as an
// unhelpful 500). Limits mirror AppDbContext's HasMaxLength where one exists.
public static class InputValidation
{
	// Trimmed, non-blank, at most maxLength characters.
	public static string RequiredText(string? value, string fieldName, int maxLength)
	{
		var trimmed = value?.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			throw new ValidationApiException($"{fieldName} is required.");
		}

		return MaxLength(trimmed, fieldName, maxLength);
	}

	// Optional (null/blank allowed, kept as given), but never longer than maxLength characters.
	public static string? OptionalText(string? value, string fieldName, int maxLength) =>
		value is null ? null : MaxLength(value, fieldName, maxLength);

	private static string MaxLength(string value, string fieldName, int maxLength) =>
		value.Length <= maxLength
			? value
			: throw new ValidationApiException($"{fieldName} must be at most {maxLength} characters.");
}
