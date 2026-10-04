using Domain.Common;

namespace Domain.Tests;

public class PhoneNumberTests
{
    [Theory]
    [InlineData("01712345678")]
    [InlineData("01712-345678")]
    [InlineData("+880 1712 345678")]
    [InlineData("8801712345678")]
    [InlineData("1712345678")]
    [InlineData("(017) 1234-5678")]
    public void Every_spelling_of_one_number_is_one_customer(string input)
    {
        var phone = PhoneNumber.Parse(input);

        Assert.True(phone.IsSuccess);
        Assert.Equal("+8801712345678", phone.Value.Value);
        Assert.Equal("01712345678", phone.Value.Local);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("01212345678")]
    [InlineData("0171234567")]
    [InlineData("+44 7911 123456")]
    [InlineData("01712abc678")]
    public void Anything_else_is_rejected(string? input)
    {
        Assert.True(PhoneNumber.Parse(input).IsFailure);
    }
}
