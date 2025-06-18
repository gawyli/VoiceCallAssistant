namespace VoiceCallAssistant.Utilities;

public static class TypeUtils
{
    public static bool InheritsFromGenericParent(this Type type, Type parentType)
    {
        if (!parentType.IsGenericType)
        {
            throw new ArgumentException($"Type {parentType.Name} is not generic", "parentType");
        }

        if ((type == null) || (type.BaseType == null))
        {
            return false;
        }
        if (type.IsGenericType && type.GetGenericTypeDefinition().IsAssignableFrom(parentType))
        {
            return true;
        }
        return type.BaseType.InheritsFromGenericParent(parentType) || type.GetInterfaces().Any(t => t.InheritsFromGenericParent(parentType));
    }
}
