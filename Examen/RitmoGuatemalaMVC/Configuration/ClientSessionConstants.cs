namespace RitmoGuatemalaMVC.Configuration;

/// <summary>
/// Valores de sesión definidos para el examen. Todavía no se usan en la interfaz.
/// </summary>
public static class ClientSessionConstants
{
    public const string UserName = "Erick Pop";
    public const int UserId = 28;

    public static DateOnly ReferenceDate => DateOnly.FromDateTime(DateTime.Today);
}
