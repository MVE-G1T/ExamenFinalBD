using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using RitmoGuatemalaMVC.Configuration;
using RitmoGuatemalaMVC.Data;

namespace RitmoGuatemalaMVC.Controllers;

public sealed class ClientesController(ClientesDashboardRepository repository) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(
        int? year,
        int? departamentoId,
        int? generoId,
        DateOnly? desde,
        DateOnly? hasta,
        CancellationToken cancellationToken)
    {
        try
        {
            var model = await repository.GetDashboardAsync(
                year,
                departamentoId,
                generoId,
                desde,
                hasta,
                ClientSessionConstants.ReferenceDate,
                cancellationToken);
            return View(model);
        }
        catch (SqlException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                "No fue posible leer db_RitmoGuate. Revisa la conexión local y vuelve a intentar.");
        }
    }
}
