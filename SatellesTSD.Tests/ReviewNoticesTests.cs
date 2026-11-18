using SatellesTSD.Core.Review;
using SatellesTSD.Core.Tax;

namespace SatellesTSD.Tests;

/// <summary>The review's notices (TYPE_TAXES_DIFFER, PAY_ON_FREE_WORK): shown only, never refusals; worked out from the
/// matrix columns and the TÖR type alone.</summary>
public class ReviewNoticesTests
{
    [Theory]
    [InlineData("10", "16", true)]
    [InlineData("10", "17", true)]  // the minimum alone differs
    [InlineData("10", "21", true)]
    [InlineData("10", "10", false)]
    public void Taxes_differ_by_the_matrix_columns(string a, string b, bool differ)
    {
        Assert.Equal(differ, ReviewChecks.TaxesDiffer(PaymentTypes.Get(a), PaymentTypes.Get(b)));
        Assert.Equal(differ, ReviewChecks.TaxesDiffer(PaymentTypes.Get(b), PaymentTypes.Get(a)));
    }

    [Fact]
    public void Every_type_compares_without_throwing_and_equals_itself()
    {
        foreach (var type in PaymentTypes.All)
        {
            Assert.False(ReviewChecks.TaxesDiffer(type, type));
            foreach (var other in PaymentTypes.All)
            {
                _ = ReviewChecks.TaxesDiffer(type, other);
            }
        }
    }

    [Fact]
    public void A_hand_type_gives_the_notice_only_when_both_types_are_known_and_their_taxes_differ()
    {
        Assert.Equal([ReviewChecks.TypeTaxesDiffer], ReviewChecks.NoticesOf("16", "10", "1", true, 100m));
        Assert.Empty(ReviewChecks.NoticesOf(null, "10", "1", true, 100m));   // no hand type
        Assert.Empty(ReviewChecks.NoticesOf("16", null, "1", true, 100m));   // the type is asked: nothing to compare with
        Assert.Empty(ReviewChecks.NoticesOf("999", "10", "1", true, 100m));  // UNKNOWN_TYPE refuses it instead
        Assert.Empty(ReviewChecks.NoticesOf("10", "10", "1", true, 100m));
        // a coded category's own code is no TÖR type's word: a sick pay (24) or a rent (57) typed by hand says nothing
        Assert.Empty(ReviewChecks.NoticesOf("10", "24", "1", false, 100m));
        Assert.Empty(ReviewChecks.NoticesOf("10", "57", null, false, 100m));
    }

    [Fact]
    public void A_salary_on_a_free_work_entry_gives_the_notice_and_nothing_else_does()
    {
        Assert.Equal([ReviewChecks.PayOnFreeWork], ReviewChecks.NoticesOf(null, "10", "501", true, 100m));
        Assert.Equal([ReviewChecks.PayOnFreeWork], ReviewChecks.NoticesOf(null, "10", " 501 ", true, 100m));
        Assert.Empty(ReviewChecks.NoticesOf(null, "10", "501", false, 100m)); // no salary
        Assert.Empty(ReviewChecks.NoticesOf(null, "10", "501", true, 0m));    // nothing paid
        Assert.Empty(ReviewChecks.NoticesOf(null, "10", "1", true, 100m));
        Assert.Empty(ReviewChecks.NoticesOf(null, "10", null, true, 100m));

        // both at once, in the fixed order
        Assert.Equal([ReviewChecks.TypeTaxesDiffer, ReviewChecks.PayOnFreeWork], ReviewChecks.NoticesOf("16", "10", "501", true, 100m));
    }

    [Fact]
    public void The_notice_codes_are_no_refusal_codes()
    {
        string[] refusals = [ReviewChecks.NoPerson, ReviewChecks.NoPayeeId, ReviewChecks.ResidencyNotChecked, ReviewChecks.NoType, ReviewChecks.TwoContracts, ReviewChecks.UnknownType, ReviewChecks.NotInPeriod, ReviewChecks.NotCounted, ReviewChecks.ForeignCurrency, ReviewChecks.DuplicateRow, ReviewChecks.MoneyIn, ReviewChecks.CodeInvalid];
        Assert.DoesNotContain(ReviewChecks.TypeTaxesDiffer, refusals);
        Assert.DoesNotContain(ReviewChecks.PayOnFreeWork, refusals);
    }
}
