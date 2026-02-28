using Membran.MultiTool.Core.Models;
using PhoneNumbers;

namespace Membran.MultiTool.Osint.Services;

public sealed class PhoneParserService
{
    private readonly PhoneNumberUtil _phoneNumberUtil = PhoneNumberUtil.GetInstance();

    public PhoneParseResult Parse(string input)
    {
        var raw = input.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new PhoneParseResult
            {
                Input = input,
                Error = "Phone number is required."
            };
        }

        try
        {
            var defaultRegion = raw.StartsWith('+') ? null : "US";
            var number = _phoneNumberUtil.Parse(raw, defaultRegion);

            var isPossible = _phoneNumberUtil.IsPossibleNumber(number);
            var isValid = _phoneNumberUtil.IsValidNumber(number);
            var region = _phoneNumberUtil.GetRegionCodeForNumber(number) ?? string.Empty;
            var type = _phoneNumberUtil.GetNumberType(number).ToString();
            var e164 = _phoneNumberUtil.Format(number, PhoneNumberFormat.E164);
            var international = _phoneNumberUtil.Format(number, PhoneNumberFormat.INTERNATIONAL);
            var national = _phoneNumberUtil.Format(number, PhoneNumberFormat.NATIONAL);

            return new PhoneParseResult
            {
                Input = input,
                E164 = e164,
                IsPossible = isPossible,
                IsValid = isValid,
                RegionCode = region,
                NumberType = type,
                Carrier = string.Empty,
                TimeZones = Array.Empty<string>(),
                InternationalFormat = international,
                NationalFormat = national
            };
        }
        catch (Exception ex)
        {
            return new PhoneParseResult
            {
                Input = input,
                Error = $"Parse failed: {ex.Message}"
            };
        }
    }
}
