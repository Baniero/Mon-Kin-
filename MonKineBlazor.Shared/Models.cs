namespace MonKineBlazor.Shared.Models;

public class PatientDto
{
    public int Id { get; set; }
    public int? CabinetId { get; set; }
    public string? CodePatient { get; set; }
    public string? DossierPatient { get; set; }
    public string Nom { get; set; } = string.Empty;
    public string? Prenom { get; set; }
    public int Age { get; set; }
    public DateTime? DateNaissance { get; set; }
    public string? Sexe { get; set; }
    public string? Telephone1 { get; set; }
    public string? Telephone2 { get; set; }
    public string? Adresse { get; set; }
    public string? Couverture { get; set; }
    public string? Racine { get; set; }
    public string? Cle { get; set; }
    public string? Qualite { get; set; }
    public string? NumeroAssuree { get; set; }
}

public class PatientProgramDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? Titre { get; set; }
    public string? TypeProgramme { get; set; }
    public string? NatureSeances { get; set; }
    public int NbSeances { get; set; }
    public int DureeSeanceMinutes { get; set; }
    public DateTime? DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public int NbSeancesParSemaine { get; set; }
    public string? CodeBureau { get; set; }
    public string? Annee { get; set; }
    public string? NumeroDecision { get; set; }
    public string? NumeroOrdre { get; set; }
    public decimal PrixUnitaire { get; set; }
    public decimal PrixEspece { get; set; }
    public decimal PrixHT { get; set; }
    public decimal TVA { get; set; }
    public decimal MontantTVA { get; set; }
    public decimal PrixTTC { get; set; }
    public string? Statut { get; set; }
    public string? Objectifs { get; set; }
    public string? Remarques { get; set; }
}

public class AppointmentDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? PatientName { get; set; }
    public int? KineId { get; set; }
    public string? KineName { get; set; }
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
    public string? Acte { get; set; }
    public string? Room { get; set; }
    public string? Status { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal CnamCovered { get; set; }
    public string? Notes { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = "kine";
    public bool Active { get; set; } = true;
    public int? CabinetId { get; set; }
    public string? CabinetName { get; set; }
}

public class UserCreateRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = "kine";
    public bool Active { get; set; } = true;
    public int? CabinetId { get; set; }
    public string Password { get; set; } = string.Empty;
}

public class UserUpdateRequestDto
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string Role { get; set; } = "kine";
    public bool Active { get; set; } = true;
    public int? CabinetId { get; set; }
    public string? Password { get; set; }
}

public class LoginRequestDto
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public UserDto User { get; set; } = new UserDto();
}

public class CashClosingDto
{
    public DateTime DateJour { get; set; }
    public decimal ExpectedAmount { get; set; }
    public decimal ActualAmount { get; set; }
    public decimal Diff { get; set; }
    public bool Validated { get; set; }
    public string? ValidatedBy { get; set; }
}

public class CashClosingRequestDto
{
    public DateTime DateJour { get; set; }
    public decimal ActualAmount { get; set; }
    public string? ValidatedBy { get; set; }
}

public class CnamRecoveryDto
{
    public string? PatientName { get; set; }
    public string? Couverture { get; set; }
    public int NbSeances { get; set; }
    public decimal MontantCnam { get; set; }
}

public class AdvanceTransactionDto
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string? PatientName { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
}

public class AdvanceTransactionRequestDto
{
    public int PatientId { get; set; }
    public decimal Amount { get; set; }
    public DateTime TransactionDate { get; set; }
    public string? Note { get; set; }
    public string? CreatedBy { get; set; }
}

public class AdvanceLotDto
{
    public int Id { get; set; }
    public int TransactionId { get; set; }
    public decimal OriginalAmount { get; set; }
    public decimal RemainingAmount { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PatientFinanceDto
{
    public int PatientId { get; set; }
    public decimal AdvanceBalance { get; set; }
    public decimal TotalAdvancePaid { get; set; }
}

public class PatientFinancialSummaryDto
{
    public int PatientId { get; set; }
    public decimal TotalAmountDue { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal OutstandingAmount { get; set; }
    public decimal AdvanceBalance { get; set; }
    public decimal TotalAdvancePaid { get; set; }
    public decimal OutstandingAfterAdvance { get; set; }
}

public class PaymentProjectionEntryDto
{
    public int AppointmentId { get; set; }
    public decimal PaidTotal { get; set; }
    public string? PaymentStatus { get; set; }
    public decimal ProjectedAdvance { get; set; }
    public bool HasProjection { get; set; }
    public string? SourceStatus { get; set; }
}

public class CnamProgramInvoiceDto
{
    public int ProgramId { get; set; }
    public int PatientId { get; set; }
    public string? PatientName { get; set; }
    public string? CodePatient { get; set; }
    public string? NumeroAssuree { get; set; }
    public string? Couverture { get; set; }
    public string? NatureSeances { get; set; }
    public int NbSeances { get; set; }
    public int DureeSeanceMinutes { get; set; }
    public DateTime? DateDebut { get; set; }
    public DateTime? DateFin { get; set; }
    public decimal PrixUnitaire { get; set; }
    public decimal PrixTTC { get; set; }
    public string? CodeBureau { get; set; }
    public string? Annee { get; set; }
    public string? NumeroDecision { get; set; }
    public string? NumeroOrdre { get; set; }
    public string? FactureNumber { get; set; }
}

public class CnamBordereauEntryDto
{
    public int ProgramId { get; set; }
    public int? BordereauNumber { get; set; }
    public string? FactureNumber { get; set; }
    public DateTime? DateFacture { get; set; }
    public string? CodePatient { get; set; }
    public string? NumeroAssuree { get; set; }
    public string? PatientName { get; set; }
    public decimal TotalTTC { get; set; }
    public DateTime? ExecutedAt { get; set; }
    public string? ExecutedBy { get; set; }
}

public class CnamBordereauExecuteRequestDto
{
    public int ProgramId { get; set; }
    public string? FactureNumber { get; set; }
    public string? ExecutedBy { get; set; }
}

public class CnamBordereauExecuteLineRequestDto
{
    public int ProgramId { get; set; }
    public string? FactureNumber { get; set; }
}

public class CnamBordereauExecuteBulkRequestDto
{
    public List<CnamBordereauExecuteLineRequestDto> Lines { get; set; } = new List<CnamBordereauExecuteLineRequestDto>();
    public List<int> ProgramIds { get; set; } = new List<int>();
    public string? ExecutedBy { get; set; }
}

public class CabinetInfoDto
{
    public int Id { get; set; }
    public string? NomCabinet { get; set; }
    public string? NumeroEmployeur { get; set; }
    public string? CodeCnam { get; set; }
    public string? Qualite { get; set; }
    public string? CodeEtablissement { get; set; }
    public string? MatriculeFiscal { get; set; }
    public string? NomEtablissement { get; set; }
    public string? AdresseCabinet { get; set; }
    public string? NomCabinetArabe { get; set; }
    public string? NomKineArabe { get; set; }
    public string? AdresseKineArabe { get; set; }
    public string? Telephone { get; set; }
    public string? Rib { get; set; }
}

public class CabinetInfoRequestDto
{
    public string? NomCabinet { get; set; }
    public string? NumeroEmployeur { get; set; }
    public string? CodeCnam { get; set; }
    public string? Qualite { get; set; }
    public string? CodeEtablissement { get; set; }
    public string? MatriculeFiscal { get; set; }
    public string? NomEtablissement { get; set; }
    public string? AdresseCabinet { get; set; }
    public string? NomCabinetArabe { get; set; }
    public string? NomKineArabe { get; set; }
    public string? AdresseKineArabe { get; set; }
    public string? Telephone { get; set; }
    public string? Rib { get; set; }
}

public class CabinetDto
{
    public int Id { get; set; }
    public string? NomCabinet { get; set; }
    public string? CodeEtablissement { get; set; }
    public string? MatriculeFiscal { get; set; }
    public string? NomEtablissement { get; set; }
    public string? NumeroEmployeur { get; set; }
    public string? CodeCnam { get; set; }
    public string? Qualite { get; set; }
    public string? AdresseCabinet { get; set; }
    public string? NomCabinetArabe { get; set; }
    public string? NomKineArabe { get; set; }
    public string? AdresseKineArabe { get; set; }
    public string? Telephone { get; set; }
    public string? Rib { get; set; }
    public string? ProgrammeTypeOptions { get; set; }
    public string? NatureSeancesOptions { get; set; }
}

public class CabinetCreateRequestDto
{
    public string? NomCabinet { get; set; }
    public string? CodeEtablissement { get; set; }
    public string? MatriculeFiscal { get; set; }
    public string? NomEtablissement { get; set; }
    public string? NumeroEmployeur { get; set; }
    public string? CodeCnam { get; set; }
    public string? Qualite { get; set; }
    public string? AdresseCabinet { get; set; }
    public string? NomCabinetArabe { get; set; }
    public string? NomKineArabe { get; set; }
    public string? AdresseKineArabe { get; set; }
    public string? Telephone { get; set; }
    public string? Rib { get; set; }
    public string? ProgrammeTypeOptions { get; set; }
    public string? NatureSeancesOptions { get; set; }
}

public class CabinetUpdateRequestDto
{
    public int Id { get; set; }
    public string? NomCabinet { get; set; }
    public string? CodeEtablissement { get; set; }
    public string? MatriculeFiscal { get; set; }
    public string? NomEtablissement { get; set; }
    public string? NumeroEmployeur { get; set; }
    public string? CodeCnam { get; set; }
    public string? Qualite { get; set; }
    public string? AdresseCabinet { get; set; }
    public string? NomCabinetArabe { get; set; }
    public string? NomKineArabe { get; set; }
    public string? AdresseKineArabe { get; set; }
    public string? Telephone { get; set; }
    public string? Rib { get; set; }
    public string? ProgrammeTypeOptions { get; set; }
    public string? NatureSeancesOptions { get; set; }
}
