using Ardalis.Specification;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository.Specifications;

public class RoutinesByUserProfileIdSpec : Specification<Routine>, ISpecification<Routine>
{
    public RoutinesByUserProfileIdSpec(string userProfileId)
    {
        this.Query.Where(r => r.UserProfileId == userProfileId);
    }
}
