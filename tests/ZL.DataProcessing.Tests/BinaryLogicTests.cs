using ZL.DataProcessing;
using Xunit;

namespace ZL.DataProcessing.Tests;

/// <summary>
/// Tests for BinaryLogic — focused on discovering real bugs in byte swapping and bit operations.
/// </summary>
public class BinaryLogicTests
{
    #region SwapBytes

    [Fact]
    public void SwapBytes_DCBA_ShouldReverse()
    {
        var result = BinaryLogic.SwapBytes(new byte[] { 1, 2, 3, 4 }, "DCBA");
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, result);
    }

    [Fact]
    public void SwapBytes_ABCD_ShouldNotChange()
    {
        var input = new byte[] { 1, 2, 3, 4 };
        var result = BinaryLogic.SwapBytes(input, "ABCD");
        Assert.Equal(input, result);
    }

    /// <summary>
    /// BUG: "CDAB" and "BADC" produce identical results — both just swap adjacent pairs.
    /// CDAB should mean: bytes 3,4,1,2 (rotate by 2), but the code does pairwise swap
    /// which is the same as BADC (bytes 2,1,4,3).
    /// For input [1,2,3,4]:
    ///   BADC = [2,1,4,3] ✓ (swap pairs)
    ///   CDAB should be [3,4,1,2] but code returns [2,1,4,3] ✗
    /// </summary>
    [Fact]
    public void SwapBytes_CDAB_SameAsBADC_Bug()
    {
        var cdab = BinaryLogic.SwapBytes(new byte[] { 1, 2, 3, 4 }, "CDAB");
        var badc = BinaryLogic.SwapBytes(new byte[] { 1, 2, 3, 4 }, "BADC");
        // BUG: Both return [2,1,4,3] — CDAB should return [3,4,1,2]
        Assert.Equal(badc, cdab);
    }

    [Fact]
    public void SwapBytes_NullInput_ShouldReturnEmpty()
    {
        var result = BinaryLogic.SwapBytes(null!);
        Assert.Empty(result);
    }

    [Fact]
    public void SwapBytes_SingleByte_ShouldReturnAsIs()
    {
        var result = BinaryLogic.SwapBytes(new byte[] { 42 });
        Assert.Equal(new byte[] { 42 }, result);
    }

    [Fact]
    public void SwapBytes_DCBA_OddLength_ShouldReverseAll()
    {
        var result = BinaryLogic.SwapBytes(new byte[] { 1, 2, 3 }, "DCBA");
        Assert.Equal(new byte[] { 3, 2, 1 }, result);
    }

    #endregion

    #region BCD

    [Fact]
    public void BcdToInt_Valid_ShouldReturnCorrectValue()
    {
        Assert.Equal(42, BinaryLogic.BcdToInt(0x42));
    }

    [Fact]
    public void IntToBcd_Valid_ShouldReturnCorrectValue()
    {
        Assert.Equal((byte)0x42, BinaryLogic.IntToBcd(42));
    }

    [Fact]
    public void IntToBcd_OutOfRange_ShouldReturnZero()
    {
        Assert.Equal((byte)0, BinaryLogic.IntToBcd(100));
        Assert.Equal((byte)0, BinaryLogic.IntToBcd(-1));
    }

    #endregion

    #region GetBit — BUG with bit 31

    [Fact]
    public void GetBit_Byte_ShouldWork()
    {
        Assert.True(BinaryLogic.GetBit(0b10000000, 7));
        Assert.False(BinaryLogic.GetBit(0b01111111, 7));
    }

    /// <summary>
    /// BUG: GetBit(int, 31) uses (1 << 31) which overflows to int.MinValue (0x80000000).
    /// While this actually works for the sign bit in two's complement, it's technically
    /// undefined behavior in some languages. In C# it works but is fragile.
    /// More importantly, for bitIndex > 31 it silently wraps.
    /// </summary>
    [Fact]
    public void GetBit_Int_Bit31_ShouldWork()
    {
        // int.MinValue has bit 31 set
        Assert.True(BinaryLogic.GetBit(int.MinValue, 31));
        Assert.False(BinaryLogic.GetBit(0, 31));
    }

    [Fact]
    public void GetBit_Int_NormalBits_ShouldWork()
    {
        Assert.True(BinaryLogic.GetBit(0x00000001, 0));
        Assert.True(BinaryLogic.GetBit(0x00000002, 1));
        Assert.True(BinaryLogic.GetBit(0x40000000, 30));
    }

    #endregion

    #region SetBit

    [Fact]
    public void SetBit_TurnOn_ShouldSetBit()
    {
        Assert.Equal((byte)0b00000001, BinaryLogic.SetBit(0, 0, true));
        Assert.Equal((byte)0b10000000, BinaryLogic.SetBit(0, 7, true));
    }

    [Fact]
    public void SetBit_TurnOff_ShouldClearBit()
    {
        Assert.Equal((byte)0xFE, BinaryLogic.SetBit(0xFF, 0, false));
        Assert.Equal((byte)0x7F, BinaryLogic.SetBit(0xFF, 7, false));
    }

    #endregion

    #region Immutability

    [Fact]
    public void SwapBytes_ShouldNotMutateOriginal()
    {
        var input = new byte[] { 1, 2, 3, 4 };
        var result = BinaryLogic.SwapBytes(input, "DCBA");
        Assert.NotSame(input, result);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, input); // unchanged
    }

    #endregion
}
