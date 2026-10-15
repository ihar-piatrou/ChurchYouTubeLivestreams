namespace ChurchYouTubeAssistant.Models;

/// <summary>
/// The fixed systematic-theology taxonomy every sermon is classified into (see
/// <see cref="Services.TheologyCategoryCatalog"/> for display names and AI-output parsing). Based on
/// the classic loci shared by Erickson's and Berkhof's systematic theologies (1-12), plus practical
/// categories the user added so every sermon - including non-doctrinal ones - has a home (13-20).
/// </summary>
public enum TheologyCategory
{
    Bibliology,
    TheologyProper,
    TrinitarianTheology,
    Christology,
    Pneumatology,
    Angelology,
    Demonology,
    TheologicalAnthropology,
    Hamartiology,
    Soteriology,
    Ecclesiology,
    Eschatology,
    ChristianEthics,
    SpiritualFormationAndSanctification,
    Discipleship,
    MissiologyAndEvangelism,
    Apologetics,
    MarriageFamilyAndParenting,
    WorshipAndPrayer,
    BiblicalHermeneutics
}
