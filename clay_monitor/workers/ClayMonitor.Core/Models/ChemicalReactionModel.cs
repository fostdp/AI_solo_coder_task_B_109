namespace ClayMonitor.Core.Models;

public class ChemicalReactionModel
{
    public string Name { get; set; } = string.Empty;
    public string[] Reactants { get; set; } = Array.Empty<string>();
    public string[] Products { get; set; } = Array.Empty<string>();
    public string[] HarmfulProducts { get; set; } = Array.Empty<string>();
    public double DeltaHkJmol { get; set; }
    public double DeltaSJmolK { get; set; }
    public double ActivationEnergyKJmol { get; set; }
    public double PreExponentialFactor { get; set; }
    public double ReactionOrder { get; set; }
    public Dictionary<string, double> Stoichiometry { get; set; } = new();
}
