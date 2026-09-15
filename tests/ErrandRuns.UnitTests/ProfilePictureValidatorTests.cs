using ErrandRuns.Api;

namespace ErrandRuns.UnitTests;

public sealed class ProfilePictureValidatorTests
{
    [Fact]
    public void Detects_supported_image_signatures()
    {
        Assert.Equal("image/jpeg", ProfilePictureValidator.DetectContentType(
            new byte[] { 0xff, 0xd8, 0xff, 0x01 }));
        Assert.Equal("image/png", ProfilePictureValidator.DetectContentType(
            new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }));
        Assert.Equal("image/webp", ProfilePictureValidator.DetectContentType(
            "RIFF1234WEBP"u8.ToArray()));
    }

    [Fact]
    public void Rejects_content_that_only_claims_to_be_an_image() =>
        Assert.Null(ProfilePictureValidator.DetectContentType("not an image"u8));
}
