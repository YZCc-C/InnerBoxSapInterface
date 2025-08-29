using ASP_Entity_Freamwork_Study.Entity;
using System.Data;

namespace Miracom.WEBCore.Service
{
    public interface MesCancelStockService
    {
        Result CancelStock(string? pickListNo, string userId);
    }
}
