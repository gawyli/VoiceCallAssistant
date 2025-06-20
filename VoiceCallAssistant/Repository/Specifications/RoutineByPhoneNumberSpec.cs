using Ardalis.Specification;
using VoiceCallAssistant.Models;

namespace VoiceCallAssistant.Repository.Specifications;

public class RoutinesByPhoneNumberSpec : Specification<Routine>, ISpecification<Routine>
{
    public RoutinesByPhoneNumberSpec(string phoneNumber)
    {
        this.Query.Where(r => r.PhoneNumber == phoneNumber);
    }
}
