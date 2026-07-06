namespace Sc2Otter.Server.Services;

using Sc2Otter.Core.Interfaces;
using System.Security.Claims;

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    
    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public int? UserId
    {
        get
        {
            var context = _httpContextAccessor.HttpContext;
            if (context == null) return null;
            
            // Check items (set by API Key middleware/filter)
            if (context.Items.TryGetValue("Sc2OtterUserId", out var userIdObj) && userIdObj is int id)
            {
                return id;
            }

            // Check claims (set by OAuth cookie)
            var claim = context.User?.FindFirst("Sc2OtterUserId");
            if (claim != null && int.TryParse(claim.Value, out var claimId))
            {
                return claimId;
            }

            return null;
        }
    }
}
