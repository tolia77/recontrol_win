using System.Threading.Tasks;
using System.Text.Json;

namespace recontrol_win
{
    internal interface IAppCommand
    {
        Task<object?> ExecuteAsync();
    }
}
