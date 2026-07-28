using InovaGed.Application.Signatures;

namespace InovaGed.Infrastructure.Signatures;

public interface ISignatureValidationOutcomeFactory
{
    SignatureValidationOutcome Create(IReadOnlyList<SignatureValidationCheck> checks);
}

public sealed class SignatureValidationOutcomeFactory : ISignatureValidationOutcomeFactory
{
    public SignatureValidationOutcome Create(IReadOnlyList<SignatureValidationCheck> checks) => CreateOutcome(checks);

    public static SignatureValidationOutcome FromChecks(IReadOnlyList<SignatureValidationCheck> checks) => CreateOutcome(checks);

    private static SignatureValidationOutcome CreateOutcome(IReadOnlyList<SignatureValidationCheck> checks)
    {
        var cryptographic = ResolveCryptographic(checks);
        var certificate = ResolveCertificate(checks);
        var trust = ResolveTrust(checks);
        var validation = ResolveValidation(cryptographic, certificate, trust, checks);
        return new SignatureValidationOutcome(cryptographic, certificate, trust, validation, SignatureConformityStatus.NOT_EVALUATED, checks);
    }

    private static SignatureValidationStatus ResolveCryptographic(IReadOnlyList<SignatureValidationCheck> checks)
    {
        if (checks.Any(c => c.Status == SignatureValidationStatus.DOCUMENT_CHANGED)) return SignatureValidationStatus.DOCUMENT_CHANGED;
        if (checks.Any(c => c.Status == SignatureValidationStatus.SIGNATURE_CORRUPTED)) return SignatureValidationStatus.SIGNATURE_CORRUPTED;
        if (checks.Any(c => c.Name is "SIGNATURE_MATH" or "MESSAGE_DIGEST" && c.Status == SignatureValidationStatus.INVALID)) return SignatureValidationStatus.INVALID;
        return checks.Any(c => c.Name == "SIGNATURE_MATH" && c.Status == SignatureValidationStatus.VALID) ? SignatureValidationStatus.VALID : SignatureValidationStatus.INDETERMINATE;
    }

    private static SignatureValidationStatus ResolveCertificate(IReadOnlyList<SignatureValidationCheck> checks)
    {
        if (checks.Any(c => c.Status == SignatureValidationStatus.EXPIRED)) return SignatureValidationStatus.EXPIRED;
        if (checks.Any(c => c.Status == SignatureValidationStatus.NOT_YET_VALID)) return SignatureValidationStatus.NOT_YET_VALID;
        if (checks.Any(c => c.Status == SignatureValidationStatus.CERTIFICATE_PURPOSE_INVALID)) return SignatureValidationStatus.CERTIFICATE_PURPOSE_INVALID;
        return checks.Any(c => c.Name == "CERTIFICATE_VALIDITY" && c.Status is SignatureValidationStatus.VALID or SignatureValidationStatus.INDETERMINATE) ? SignatureValidationStatus.VALID : SignatureValidationStatus.NOT_VERIFIABLE;
    }

    private static SignatureValidationStatus ResolveTrust(IReadOnlyList<SignatureValidationCheck> checks)
    {
        if (checks.Any(c => c.Status == SignatureValidationStatus.UNTRUSTED_CHAIN)) return SignatureValidationStatus.UNTRUSTED_CHAIN;
        if (checks.Any(c => c.Status is SignatureValidationStatus.TRUSTED_SYSTEM_CHAIN or SignatureValidationStatus.TRUSTED_TEST_CHAIN)) return SignatureValidationStatus.TRUSTED_SYSTEM_CHAIN;
        return SignatureValidationStatus.NOT_VERIFIABLE;
    }

    private static SignatureValidationStatus ResolveValidation(SignatureValidationStatus cryptographic, SignatureValidationStatus certificate, SignatureValidationStatus trust, IReadOnlyList<SignatureValidationCheck> checks)
    {
        if (cryptographic is SignatureValidationStatus.DOCUMENT_CHANGED or SignatureValidationStatus.SIGNATURE_CORRUPTED or SignatureValidationStatus.INVALID) return SignatureValidationStatus.INVALID;
        if (certificate is SignatureValidationStatus.EXPIRED or SignatureValidationStatus.NOT_YET_VALID or SignatureValidationStatus.CERTIFICATE_PURPOSE_INVALID) return SignatureValidationStatus.INVALID;
        if (trust == SignatureValidationStatus.UNTRUSTED_CHAIN) return SignatureValidationStatus.INDETERMINATE;
        return cryptographic == SignatureValidationStatus.VALID && certificate == SignatureValidationStatus.VALID && trust is SignatureValidationStatus.TRUSTED_SYSTEM_CHAIN or SignatureValidationStatus.TRUSTED_TEST_CHAIN ? SignatureValidationStatus.VALID : SignatureValidationStatus.INDETERMINATE;
    }
}
