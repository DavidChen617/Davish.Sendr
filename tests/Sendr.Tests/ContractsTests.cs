using System.Reflection;
using Davish.JustBdd;
using Davish.Sendr;
using Sendr.TestFeatures;

namespace Sendr.Tests;

[Feature<ContractsFeature>]
public class ContractsTests
{
    public static TheoryData<Type> PublicContractTypes => new()
    {
        typeof(ISender),
        typeof(IStreamSender),
        typeof(IPublisher),
        typeof(IRequest),
        typeof(IRequest<>),
        typeof(IStreamRequest<>),
        typeof(ICommand),
        typeof(ICommand<>),
        typeof(IQuery<>),
        typeof(INotification),
        typeof(IRequestHandler<>),
        typeof(IRequestHandler<,>),
        typeof(IStreamRequestHandler<,>),
        typeof(ICommandHandler<>),
        typeof(ICommandHandler<,>),
        typeof(IQueryHandler<,>),
        typeof(INotificationHandler<>),
        typeof(IRequestDecorator),
        typeof(IRequestDecorator.WithResponse),
        typeof(IStreamRequestDecorator),
        typeof(ICommandDecorator),
        typeof(ICommandDecorator.WithResponse),
        typeof(IQueryDecorator),
        typeof(INotificationDecorator),
        typeof(RequestHandlerDelegate),
        typeof(RequestHandlerDelegate<>),
        typeof(StreamHandlerDelegate<>),
        typeof(NotificationHandlerDelegate),
    };

    [Theory]
    [MemberData(nameof(PublicContractTypes))]
    public void GivenPublicContractType_WhenInspectingItsAssembly_ThenItIsDavishSendrAbstractions(Type contract)
    {
        new Scenario()
            .Given($"The public contract type {contract.Name}", () => contract)
            .When("Its assembly is inspected", type => type.Assembly.GetName().Name)
            .Then("It lives in the abstractions assembly")
                .Equal("The assembly is Davish.Sendr.Abstractions", "Davish.Sendr.Abstractions", assemblyName => assemblyName)
            .Run();
    }

    [Fact]
    public void GivenAbstractionsAssembly_WhenInspectingReferencedAssemblies_ThenDavishSendrIsNotReferenced()
    {
        new Scenario()
            .Given("The Davish.Sendr.Abstractions assembly", () => typeof(ISender).Assembly)
            .When("Its referenced assemblies are listed", ReferencedAssemblyNames)
            .Then("The implementation assembly is not among them")
                .DoesNotContain("Davish.Sendr is not referenced", "Davish.Sendr", names => names)
            .Run();
    }

    [Fact]
    public void GivenDecorateWithAttribute_WhenInspectingItsAssembly_ThenItIsDavishSendrSharedWithoutReferenceToDavishSendr()
    {
        new Scenario()
            .Given("The DecorateWith attribute type, shipped inside the abstractions package", () => typeof(DecorateWithAttribute<>).Assembly)
            .When("Its assembly is inspected", assembly => (Name: assembly.GetName().Name, References: ReferencedAssemblyNames(assembly)))
            .Then("It lives in the shared assembly")
                .Equal("The assembly is Davish.Sendr.Shared", "Davish.Sendr.Shared", inspected => inspected.Name)
            .And("That assembly does not reference the implementation")
                .DoesNotContain("Davish.Sendr is not referenced", "Davish.Sendr", inspected => inspected.References)
            .Run();
    }

    [Fact]
    public void GivenISender_WhenCheckingAssignabilityToIStreamSender_ThenISenderIsAnIStreamSender()
    {
        new Scenario()
            .Given("The ISender and IStreamSender interfaces", () => (Sender: typeof(ISender), StreamSender: typeof(IStreamSender)))
            .When("ISender is checked for assignability to IStreamSender", types => types.StreamSender.IsAssignableFrom(types.Sender))
            .Then("ISender can be used as an IStreamSender")
                .True("ISender is assignable to IStreamSender", assignable => assignable)
            .Run();
    }

    public static TheoryData<Type, Type> GenericToNonGenericBases => new()
    {
        { typeof(IRequest<int>), typeof(IRequest) },
        { typeof(ICommand<int>), typeof(ICommand) },
    };

    [Theory]
    [MemberData(nameof(GenericToNonGenericBases))]
    public void GivenGenericMessageInterface_WhenCheckingAssignabilityToNonGenericBase_ThenItIsAssignable(Type generic, Type nonGenericBase)
    {
        new Scenario()
            .Given($"The closed generic message interface {Display(generic)}", () => generic)
            .When($"It is checked for assignability to {Display(nonGenericBase)}", type => nonGenericBase.IsAssignableFrom(type))
            .Then("It is assignable to its non-generic base")
                .True($"{Display(generic)} is assignable to {Display(nonGenericBase)}", assignable => assignable)
            .Run();
    }

    public static TheoryData<Type, Type> UnrelatedMessageFamilies => new()
    {
        { typeof(ICommand), typeof(IRequest) },
        { typeof(ICommand<int>), typeof(IRequest<int>) },
        { typeof(IQuery<int>), typeof(IRequest<int>) },
        { typeof(IQuery<int>), typeof(IRequest) },
        { typeof(IQuery<int>), typeof(ICommand) },
        { typeof(IQuery<int>), typeof(ICommand<int>) },
        { typeof(IStreamRequest<int>), typeof(IRequest) },
        { typeof(IStreamRequest<int>), typeof(ICommand) },
        { typeof(IStreamRequest<int>), typeof(IQuery<int>) },
    };

    [Theory]
    [MemberData(nameof(UnrelatedMessageFamilies))]
    public void GivenTwoMessageFamilies_WhenCheckingAssignability_ThenNeitherIsAssignableToTheOther(Type left, Type right)
    {
        new Scenario()
            .Given($"The message interfaces {Display(left)} and {Display(right)}", () => (Left: left, Right: right))
            .When("Assignability is checked in both directions", types =>
                (LeftToRight: types.Right.IsAssignableFrom(types.Left), RightToLeft: types.Left.IsAssignableFrom(types.Right)))
            .Then("Neither direction is assignable")
                .False($"{Display(left)} is not assignable to {Display(right)}", result => result.LeftToRight)
                .False($"{Display(right)} is not assignable to {Display(left)}", result => result.RightToLeft)
            .Run();
    }

    public static TheoryData<int> DecorateWithArities => new() { 1, 2, 3, 4, 5, 6, 7, 8 };

    [Theory]
    [MemberData(nameof(DecorateWithArities))]
    public void GivenDecorateWithAttributeOfAnyArity_WhenReadingAttributeUsage_ThenClassOnlyNotMultipleNotInherited(int arity)
    {
        new Scenario()
            .Given($"The DecorateWith attribute with {arity} type parameter(s)", () =>
                typeof(DecorateWithAttribute<>).Assembly.GetType($"Davish.Sendr.DecorateWithAttribute`{arity}", throwOnError: true)!)
            .When("Its AttributeUsage is read", type => type.GetCustomAttribute<AttributeUsageAttribute>()!)
            .Then("It can only be applied to classes")
                .Equal("ValidOn is Class", AttributeTargets.Class, usage => usage.ValidOn)
            .And("It cannot be applied more than once")
                .False("AllowMultiple is false", usage => usage.AllowMultiple)
            .And("It is not inherited")
                .False("Inherited is false", usage => usage.Inherited)
            .Run();
    }

    private static string[] ReferencedAssemblyNames(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(name => name.Name!).ToArray();

    private static string Display(Type type) =>
        type.IsGenericType
            ? $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", type.GetGenericArguments().Select(Display))}>"
            : type.Name;
}
