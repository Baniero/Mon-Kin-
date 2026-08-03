namespace MonKineBlazor.Shared.Models;

public class PatientDto
{
    public int Id { get; set; }
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
}
