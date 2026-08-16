namespace ResumeForge.Infrastructure.Ai;

/// <summary>
/// Match phrases for each canonical autofill key, ported verbatim from
/// <c>extension/src/heuristics/synonyms.ts</c> so <see cref="AutofillFieldMatcher"/> judges
/// a field escalated to the model fallback by the same rules the extension's own tier-2
/// matcher uses. Keep the two tables in sync by hand — there is no shared build step
/// between the extension and this backend.
/// </summary>
internal static class AutofillFieldSynonyms
{
    /// <summary>Canonical key → synonym phrases, each tokenized and matched by full containment.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> Synonyms = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
    {
        ["firstName"] = ["first name", "firstname", "given name", "fname", "legal first name"],
        ["lastName"] = ["last name", "lastname", "surname", "family name", "lname", "legal last name"],
        ["fullName"] = ["full name", "your name", "applicant name", "candidate name", "name"],
        ["preferredName"] = ["preferred name", "nickname", "preferred first name", "name you go by", "goes by"],
        ["email"] = ["email", "e-mail", "email address", "work email", "personal email", "e-mail address"],
        ["phone"] = [
            "phone", "phone number", "telephone", "telephone number", "mobile", "mobile number",
            "mobile phone", "cell phone", "cell number", "contact number",
        ],
        ["addressLine1"] = ["address", "street address", "address line 1", "home address", "mailing address"],
        ["addressLine2"] = ["address line 2", "apartment number", "apt suite", "suite number", "unit number"],
        ["city"] = ["city", "town", "city town"],
        ["state"] = ["state", "province", "state province", "region"],
        ["postalCode"] = ["postal code", "zip code", "zipcode", "zip", "postcode"],
        ["country"] = ["country", "country region", "nation"],
        ["linkedin"] = ["linkedin", "linkedin profile", "linkedin url", "linkedin link", "linkedin address"],
        ["github"] = ["github", "github profile", "github url", "github link"],
        ["portfolio"] = ["portfolio", "portfolio url", "portfolio link", "personal portfolio", "work samples"],
        ["website"] = ["website", "personal website", "web site", "personal site", "homepage"],
        ["currentCompany"] = ["current company", "current employer", "employer name", "company name", "present employer"],
        ["currentTitle"] = ["current title", "current job title", "current role", "current position", "job title", "present title"],
        ["yearsExperience"] = ["years of experience", "years experience", "total years of experience", "years of relevant experience"],
        ["workAuthorization"] = [
            "work authorization", "authorized to work", "legally authorized to work", "work permit",
            "right to work", "employment authorization",
        ],
        ["requiresSponsorship"] = [
            "visa sponsorship", "require sponsorship", "need sponsorship", "need visa sponsorship",
            "require visa sponsorship", "future require sponsorship", "sponsorship now future",
        ],
        ["willingToRelocate"] = ["willing to relocate", "open to relocation", "able to relocate", "relocation willingness"],
        ["noticePeriod"] = ["notice period", "notice period required", "how much notice", "weeks notice", "availability notice"],
        ["desiredSalary"] = [
            "desired salary", "salary expectation", "expected salary", "salary expectations",
            "compensation expectation", "salary requirement", "expected compensation",
        ],
        ["availableStartDate"] = [
            "available start date", "start date", "earliest start date", "when can you start",
            "availability date", "available to start",
        ],
        ["gender"] = ["gender", "gender identity"],
        ["ethnicity"] = ["ethnicity", "race ethnicity", "racial identity", "race"],
        ["veteranStatus"] = ["veteran status", "protected veteran", "military veteran status"],
        ["disabilityStatus"] = ["disability status", "disability", "self identify disability"],
        ["howDidYouHear"] = [
            "how did you hear about us", "how did you hear about this position", "how did you hear",
            "referral source", "where did you hear about this job",
        ],
        ["referredBy"] = ["referred by", "who referred you", "employee referral", "referral name"],
    };

    /// <summary>
    /// WHATWG <c>autocomplete</c> attribute tokens that map directly to a canonical key. When
    /// a field's <c>autocomplete</c> value's last token (the "autofill field name", per the
    /// spec — leading <c>section-*</c>/<c>shipping</c>/<c>billing</c> tokens are ignored) is
    /// a key of this map, the matcher short-circuits to confidence 1.0.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> AutocompleteMap = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["given-name"] = "firstName",
        ["family-name"] = "lastName",
        ["name"] = "fullName",
        ["nickname"] = "preferredName",
        ["email"] = "email",
        ["tel"] = "phone",
        ["tel-national"] = "phone",
        ["address-line1"] = "addressLine1",
        ["address-line2"] = "addressLine2",
        ["address-level2"] = "city",
        ["address-level1"] = "state",
        ["postal-code"] = "postalCode",
        ["country"] = "country",
        ["country-name"] = "country",
        ["organization"] = "currentCompany",
        ["organization-title"] = "currentTitle",
        ["url"] = "website",
    };
}
