using ASP_Entity_Freamwork_Study.Entity;
using System.Data;

namespace Miracom.WEBCore.Service
{
    public interface InBoxWarehouseService
    {
        List<Dictionary<string, object>> GetInBoxData(string boxId, string productModel, string pid, string poNo, string moNo, string lotId, string binGrade);
    }
}
