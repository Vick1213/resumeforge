namespace ResumeForge.Api.Tests.TestSupport;

/// <summary>
/// Writes a small, self-contained knowledge-base fixture (CONTRACTS.md §3 markdown format)
/// to a temp directory, isolated from the repository's real <c>profile/</c> sample data so
/// integration tests never read or mutate it.
/// </summary>
internal static class TestProfileFixture
{
    /// <summary>Writes the fixture profile under <paramref name="profileRoot"/>.</summary>
    public static void Write(string profileRoot)
    {
        Directory.CreateDirectory(profileRoot);
        Directory.CreateDirectory(Path.Combine(profileRoot, "experience"));
        Directory.CreateDirectory(Path.Combine(profileRoot, "projects"));
        Directory.CreateDirectory(Path.Combine(profileRoot, "education"));
        Directory.CreateDirectory(Path.Combine(profileRoot, "certifications"));

        File.WriteAllText(Path.Combine(profileRoot, "basics.md"), """
            ---
            fullName: Test Candidate
            headline: Backend Engineer
            email: test.candidate@example.com
            phone: +1 (555) 555-0100
            location: Remote
            website: https://example.com
            linkedin: https://linkedin.com/in/test-candidate
            github: https://github.com/test-candidate
            ---

            Backend engineer with experience building HTTP APIs in C# and distributed systems.
            """);

        File.WriteAllText(Path.Combine(profileRoot, "experience", "acme-corp.md"), """
            ---
            type: experience
            role: Senior Backend Engineer
            organization: Acme Corp
            location: Remote
            startDate: 2021-01
            endDate: present
            tech: [C#, .NET, PostgreSQL]
            tags: [backend]
            ---

            - Reduced checkout p99 latency from 800ms to 150ms by parallelizing downstream calls.
              - Cut checkout p99 latency 5x with a bounded parallel scatter-gather.
            - Led migration of the payments service from .NET 6 to .NET 8.
            """);

        File.WriteAllText(Path.Combine(profileRoot, "projects", "graph-runner.md"), """
            ---
            type: project
            name: Graph Runner
            tagline: A small DAG executor
            url: https://example.com/graph-runner
            repoUrl: https://github.com/test-candidate/graph-runner
            startDate: 2023-01
            endDate: present
            tech: [TypeScript, Node.js]
            tags: [orchestration]
            source: manual
            ---

            - Built a scheduler that runs independent stages concurrently.
            """);

        File.WriteAllText(Path.Combine(profileRoot, "education", "state-university.md"), """
            ---
            type: education
            institution: State University
            credential: B.S. Computer Science
            location: Remote
            startDate: 2015-09
            endDate: 2019-06
            gpa: 3.7
            ---

            - Graduated with honors.
            """);

        File.WriteAllText(Path.Combine(profileRoot, "certifications", "az-204.md"), """
            ---
            type: certification
            name: Azure Developer Associate (AZ-204)
            issuer: Microsoft
            issuedOn: 2022-05
            credentialUrl: https://learn.microsoft.com/credentials/example
            ---
            """);
    }
}
