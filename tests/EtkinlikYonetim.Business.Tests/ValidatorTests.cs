using EtkinlikYonetim.Business.Services;
using EtkinlikYonetim.Business.Validation;
using EtkinlikYonetim.Domain.Entities;
using FluentValidation;

namespace EtkinlikYonetim.Business.Tests;

/// <summary>
/// The input rules on their own: boundaries, the order of the messages when several rules fail, and the
/// unchanged Turkish texts. Each validator reports only its first failure.
/// </summary>
public sealed class ValidatorTests
{
    private const string Password = "Gizli.Sifre1";
    private const string ValidNationalId = "10000000146";
    private static readonly DateOnly Today = new(2026, 3, 2);

    [Theory]
    [InlineData("", Password, Password, nameof(AuthService.RegisterInputRequired))]
    [InlineData("yeni", "", "", nameof(AuthService.RegisterInputRequired))]
    [InlineData("yeni", "   ", "   ", nameof(AuthService.RegisterInputRequired))]
    [InlineData("yeni", null, null, nameof(AuthService.RegisterInputRequired))]
    [InlineData("ye ni", Password, Password, nameof(AuthService.UserNameInvalid))]
    [InlineData("yeni1", Password, Password, nameof(AuthService.UserNameInvalid))]
    [InlineData("yeni", Password, "Gizli.Sifre2", nameof(AuthService.PasswordsDoNotMatch))]
    [InlineData("yeni", Password, null, nameof(AuthService.PasswordsDoNotMatch))]
    [InlineData("yeni", "1234567", "1234567", nameof(AuthService.PasswordInvalid))]
    [InlineData("ye ni", "1234567", "7654321", nameof(AuthService.UserNameInvalid))] // the first failing rule wins
    [InlineData("yeni", "1234567", "7654321", nameof(AuthService.PasswordsDoNotMatch))]
    public void Account_InvalidInput_ReportsTheFirstFailingRule(string userName, string? password, string? repeat, string expected) =>
        AssertFails(new AccountInputValidator(), AccountInput.Create(userName, password, repeat), AuthMessage(expected));

    [Fact]
    public void Account_Boundaries_AreInclusive()
    {
        var validator = new AccountInputValidator();

        Assert.True(validator.Validate(AccountInput.Create(new string('a', InputRules.UserNameMaxLength), Password, Password)).IsValid);
        AssertFails(validator, AccountInput.Create(new string('a', InputRules.UserNameMaxLength + 1), Password, Password), AuthService.UserNameInvalid);
        Assert.True(validator.Validate(AccountInput.Create("Şule", "12345678", "12345678")).IsValid);
        Assert.True(validator.Validate(AccountInput.Create("yeni", new string('x', 64), new string('x', 64))).IsValid);
        AssertFails(validator, AccountInput.Create("yeni", new string('x', 65), new string('x', 65)), AuthService.PasswordInvalid);
    }

    [Fact]
    public void Account_Create_TrimsTheNameButKeepsThePasswordsAsTyped()
    {
        var input = AccountInput.Create("  kurucu ", " Gizli.Sifre1 ", " Gizli.Sifre1 ");

        Assert.Equal("kurucu", input.UserName);
        Assert.Equal(" Gizli.Sifre1 ", input.Password);
        Assert.Equal(" Gizli.Sifre1 ", input.PasswordRepeat);
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData(null, Password)]
    [InlineData("kullanici", "")]
    [InlineData("kullanici", null)]
    public void SignIn_MissingInput_IsRefusedWithOneMessage(string? userName, string? password) =>
        AssertFails(new SignInInputValidator(), SignInInput.Create(userName, password), AuthService.InputRequired);

    [Fact]
    public void SignIn_AcceptsWhatAnExistingAccountCanHold()
    {
        var validator = new SignInInputValidator();

        // Legacy accounts may have short passwords and names up to the old column size.
        Assert.True(validator.Validate(SignInInput.Create(new string('a', InputRules.StoredUserNameMaxLength), "kisa")).IsValid);
        Assert.True(validator.Validate(SignInInput.Create("kullanici", new string('x', 100))).IsValid);
        AssertFails(validator, SignInInput.Create(new string('a', InputRules.StoredUserNameMaxLength + 1), Password), AuthService.InputRequired);
        AssertFails(validator, SignInInput.Create("kullanici", new string('x', 101)), AuthService.InputRequired);
    }

    [Theory]
    [InlineData("", "Deniz Örnek", "d@example.com", ParticipantService.FieldsRequired)]
    [InlineData(ValidNationalId, " ", "d@example.com", ParticipantService.FieldsRequired)]
    [InlineData(ValidNationalId, "Deniz Örnek", "", ParticipantService.FieldsRequired)]
    [InlineData("10000000147", "Deniz Örnek", "d@example.com", ParticipantService.NationalIdInvalid)]
    [InlineData("00000000146", "Deniz Örnek", "d@example.com", ParticipantService.NationalIdInvalid)]
    [InlineData("1000000014", "Deniz Örnek", "d@example.com", ParticipantService.NationalIdInvalid)]
    [InlineData(ValidNationalId, "Deniz  Örnek", "d@example.com", ParticipantService.FullNameInvalid)]
    [InlineData(ValidNationalId, "Deniz2", "d@example.com", ParticipantService.FullNameInvalid)]
    [InlineData(ValidNationalId, "Deniz Örnek", "d@example", ParticipantService.EmailInvalid)]
    [InlineData(ValidNationalId, "Deniz Örnek", "d @example.com", ParticipantService.EmailInvalid)]
    [InlineData("10000000147", "Deniz2", "d@example", ParticipantService.NationalIdInvalid)] // the first failing rule wins
    [InlineData(ValidNationalId, "Deniz2", "d@example", ParticipantService.FullNameInvalid)]
    public void Participant_InvalidInput_ReportsTheFirstFailingRule(string nationalId, string fullName, string email, string expected) =>
        AssertFails(new ParticipantInputValidator(), ParticipantInput.Create(nationalId, fullName, email), expected);

    [Fact]
    public void Participant_Boundaries_AndTrimming()
    {
        var validator = new ParticipantInputValidator();
        var input = ParticipantInput.Create($" {ValidNationalId} ", " Deniz Örnek ", " d@example.com ");

        Assert.Equal((ValidNationalId, "Deniz Örnek", "d@example.com"), (input.NationalId, input.FullName, input.Email));
        Assert.True(validator.Validate(input).IsValid);
        Assert.True(validator.Validate(ParticipantInput.Create(ValidNationalId, new string('a', InputRules.PersonNameMaxLength), "d@example.com")).IsValid);
        AssertFails(validator, ParticipantInput.Create(ValidNationalId, new string('a', InputRules.PersonNameMaxLength + 1), "d@example.com"), ParticipantService.FullNameInvalid);
        var longMail = new string('a', InputRules.EmailMaxLength - "@example.com".Length) + "@example.com";
        Assert.True(validator.Validate(ParticipantInput.Create(ValidNationalId, "Deniz", longMail)).IsValid);
        AssertFails(validator, ParticipantInput.Create(ValidNationalId, "Deniz", "a" + longMail), ParticipantService.EmailInvalid);
    }

    [Theory]
    [InlineData("", 0, 10, nameof(EventService.NameRequired))]
    [InlineData("   ", 0, 10, nameof(EventService.NameRequired))]
    [InlineData("Konser", -1, 10, nameof(EventService.PastDate))]
    [InlineData("Konser", 0, 0, nameof(EventService.CapacityInvalid))]
    [InlineData("Konser", 0, -5, nameof(EventService.CapacityInvalid))]
    [InlineData("Konser", 0, 1001, nameof(EventService.CapacityInvalid))]
    [InlineData("", -1, 0, nameof(EventService.NameRequired))] // the first failing rule wins
    [InlineData("Konser", -1, 0, nameof(EventService.PastDate))]
    public void Event_InvalidInput_ReportsTheFirstFailingRule(string name, int days, int capacity, string expected) =>
        AssertFails(new EventInputValidator(), EventInput.Create(name, Today.AddDays(days).ToDateTime(new TimeOnly(9, 0)), capacity, Today), EventMessage(expected));

    [Theory]
    [InlineData(0, InputRules.MinCapacity)]
    [InlineData(0, InputRules.MaxCapacity)]
    [InlineData(400, 50)]
    public void Event_TodayAndCapacityBounds_AreValid(int days, int capacity) =>
        Assert.True(new EventInputValidator().Validate(EventInput.Create("Konser", Today.AddDays(days).ToDateTime(new TimeOnly(23, 59)), capacity, Today)).IsValid);

    [Fact]
    public void Event_NameBoundary_AndTrimming()
    {
        var validator = new EventInputValidator();

        Assert.Equal("Konser", EventInput.Create("  Konser ", Today.ToDateTime(TimeOnly.MinValue), 10, Today).Name);
        Assert.True(validator.Validate(EventInput.Create(new string('a', InputRules.EventNameMaxLength), Today.ToDateTime(TimeOnly.MinValue), 10, Today)).IsValid);
        AssertFails(validator, EventInput.Create(new string('a', InputRules.EventNameMaxLength + 1), Today.ToDateTime(TimeOnly.MinValue), 10, Today), EventService.NameTooLong);
    }

    [Theory]
    [InlineData(0, ValidNationalId, RegistrationService.EventRequired)]
    [InlineData(-3, ValidNationalId, RegistrationService.EventRequired)]
    [InlineData(1, "", RegistrationService.NationalIdFormat)]
    [InlineData(1, "1000000014", RegistrationService.NationalIdFormat)]
    [InlineData(1, "1000000014a", RegistrationService.NationalIdFormat)]
    [InlineData(0, "", RegistrationService.EventRequired)]
    public void RegistrationRequest_InvalidInput_ReportsTheFirstFailingRule(int eventId, string nationalId, string expected) =>
        AssertFails(new RegistrationRequestInputValidator(), RegistrationRequestInput.Create(eventId, nationalId), expected);

    [Fact]
    public void RegistrationRequest_ChecksTheFormatOnly()
    {
        // Rows of the original application may have a wrong checksum; they can still be registered.
        Assert.True(new RegistrationRequestInputValidator().Validate(RegistrationRequestInput.Create(1, " 10000000147 ")).IsValid);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-400)]
    public void RegistrationTarget_Past_IsRefused(int days) =>
        AssertFails(new RegistrationTargetInputValidator(), new RegistrationTargetInput(Today.AddDays(days), Today), RegistrationService.PastEvent);

    /// <summary>The seats play no part any more: a full event leads to its waiting list.</summary>
    [Fact]
    public void RegistrationTarget_Today_IsValid() =>
        Assert.True(new RegistrationTargetInputValidator().Validate(new RegistrationTargetInput(Today, Today)).IsValid);

    [Theory]
    [InlineData(RegistrationStatus.Cancelled, 0, RegistrationService.AlreadyCancelled)]
    [InlineData(RegistrationStatus.Active, -1, RegistrationService.PastCannotCancel)]
    [InlineData(RegistrationStatus.Cancelled, -1, RegistrationService.AlreadyCancelled)]
    public void Cancellation_CancelledOrPast_IsRefused(RegistrationStatus status, int days, string expected) =>
        AssertFails(new CancellationInputValidator(), new CancellationInput(status, Today.AddDays(days), Today), expected);

    [Theory]
    [InlineData(RegistrationStatus.Active)]
    [InlineData(RegistrationStatus.Waiting)]
    public void Cancellation_SeatOrWaitingPlaceToday_IsValid(RegistrationStatus status) =>
        Assert.True(new CancellationInputValidator().Validate(new CancellationInput(status, Today, Today)).IsValid);

    /// <summary>The texts the screens show stay exactly as they were before the validators.</summary>
    [Fact]
    public void Messages_AreTheUnchangedTurkishTexts()
    {
        Assert.Equal("Kullanıcı adı ve şifre boş olamaz.", AuthService.RegisterInputRequired);
        Assert.Equal("Lütfen kullanıcı adı ve şifrenizi girin.", AuthService.InputRequired);
        Assert.Equal("Kullanıcı adı yalnızca harflerden oluşmalı ve en fazla 32 karakter olmalıdır.", AuthService.UserNameInvalid);
        Assert.Equal("Şifreler uyuşmuyor.", AuthService.PasswordsDoNotMatch);
        Assert.Equal("Şifre en az 8, en fazla 64 karakter olmalıdır.", AuthService.PasswordInvalid);
        Assert.Equal("Lütfen tüm alanları doldurun.", ParticipantService.FieldsRequired);
        Assert.Equal("Lütfen geçerli bir T.C. Kimlik No girin.", ParticipantService.NationalIdInvalid);
        Assert.Equal("Ad soyad yalnızca harf ve tek boşluk içerebilir ve en fazla 64 karakter olabilir.", ParticipantService.FullNameInvalid);
        Assert.Equal("Geçersiz e-posta adresi!", ParticipantService.EmailInvalid);
        Assert.Equal("Etkinlik adı boş bırakılamaz.", EventService.NameRequired);
        Assert.Equal("Etkinlik adı en fazla 80 karakter olabilir.", EventService.NameTooLong);
        Assert.Equal("Geçmiş tarihli etkinlik eklenemez.", EventService.PastDate);
        Assert.Equal("Kapasite 1 ile 1000 arasında olmalıdır.", EventService.CapacityInvalid);
        Assert.Equal("Lütfen bir etkinlik seçin.", RegistrationService.EventRequired);
        Assert.Equal("Geçerli bir 11 haneli TC Kimlik No girin.", RegistrationService.NationalIdFormat);
        Assert.Equal("Geçmiş tarihli bir etkinliğe kayıt yapılamaz.", RegistrationService.PastEvent);
        Assert.Equal("Bu katılımcı bu etkinliğe zaten kayıtlı veya bekleme listesinde.", RegistrationService.AlreadyRegistered);
        Assert.Equal("Bu kayıt zaten iptal edilmiş.", RegistrationService.AlreadyCancelled);
        Assert.Equal("Geçmiş bir etkinliğin kaydı iptal edilemez.", RegistrationService.PastCannotCancel);
    }

    private static void AssertFails<T>(IValidator<T> validator, T input, string expected)
    {
        var result = validator.Validate(input);

        Assert.False(result.IsValid);
        Assert.Equal(expected, result.Errors[0].ErrorMessage);
        var refused = Assert.Throws<BusinessRuleException>(() => validator.ValidateOrThrow(input));
        Assert.Equal(expected, refused.Message);
    }

    private static string AuthMessage(string name) => name switch
    {
        nameof(AuthService.RegisterInputRequired) => AuthService.RegisterInputRequired,
        nameof(AuthService.UserNameInvalid) => AuthService.UserNameInvalid,
        nameof(AuthService.PasswordsDoNotMatch) => AuthService.PasswordsDoNotMatch,
        nameof(AuthService.PasswordInvalid) => AuthService.PasswordInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static string EventMessage(string name) => name switch
    {
        nameof(EventService.NameRequired) => EventService.NameRequired,
        nameof(EventService.PastDate) => EventService.PastDate,
        nameof(EventService.CapacityInvalid) => EventService.CapacityInvalid,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };
}
