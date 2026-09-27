using System.Text;
using Domain.Common;

namespace Domain.Customers;

/// <summary>
/// A Bangladeshi mobile number in E.164 form (+8801XXXXXXXXX). The phone number is how the system recognises
/// the same customer across shops, so every spelling a merchant might send has to reduce to one value:
/// "01712-345678", "8801712345678", "+880 1712 345678" and "1712345678" are all +8801712345678.
/// </summary>
public readonly record struct PhoneNumber
{
    private PhoneNumber(string value)
    {
        Value = value;
    }

    public string Value { get; }

    /// <summary>The local form customers recognise: 01712345678.</summary>
    public string Local => "0" + Value[4..];

    public static Result<PhoneNumber> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Error.Validation("phone.required", "A phone number is required.");
        }

        var digits = new StringBuilder(input.Length);
        foreach (var character in input)
        {
            if (char.IsAsciiDigit(character))
            {
                digits.Append(character);
            }
            else if (character is not (' ' or '-' or '(' or ')' or '.' or '+'))
            {
                return Invalid();
            }
        }

        var national = digits.ToString() switch
        {
            { Length: 13 } value when value.StartsWith("880") => value[3..],
            { Length: 11 } value when value.StartsWith('0') => value[1..],
            { Length: 10 } value => value,
            _ => null
        };

        // Mobile numbers are 1 followed by an operator digit 3-9 and eight more digits
        if (national is null || national[0] != '1' || national[1] < '3')
        {
            return Invalid();
        }

        return new PhoneNumber("+880" + national);
    }

    public override string ToString()
    {
        return Value;
    }

    private static Error Invalid()
    {
        return Error.Validation("phone.invalid", "Enter a Bangladeshi mobile number such as 01712345678.");
    }
}
