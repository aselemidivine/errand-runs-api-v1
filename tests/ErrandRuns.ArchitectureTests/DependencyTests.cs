namespace ErrandRuns.ArchitectureTests;
public sealed class DependencyTests
{
    [Fact] public void Domain_has_no_forbidden_project_references(){var refs=typeof(ErrandRuns.Domain.Errands.Errand).Assembly.GetReferencedAssemblies().Select(x=>x.Name).ToArray();Assert.DoesNotContain("ErrandRuns.Infrastructure",refs);Assert.DoesNotContain("ErrandRuns.Api",refs);}
    [Fact] public void Application_does_not_reference_api_or_infrastructure(){var refs=typeof(ErrandRuns.Application.ErrandService).Assembly.GetReferencedAssemblies().Select(x=>x.Name).ToArray();Assert.DoesNotContain("ErrandRuns.Infrastructure",refs);Assert.DoesNotContain("ErrandRuns.Api",refs);}
}
