using CpuAffinityManager.Engine;

namespace CpuAffinityManager.Tests;

public class RuleConfigTests
{
    [Fact]
    public void Save_WritesChineseTextWithoutUnicodeEscapes()
    {
        string filePath = Path.Combine(Path.GetTempPath(), $"cpu-affinity-{Guid.NewGuid():N}.json");

        try
        {
            var config = new RuleConfig
            {
                Rules = [new RuleEntry { Name = "游戏绑定大核" }]
            };

            config.Save(filePath);

            string json = File.ReadAllText(filePath);
            Assert.Contains("游戏绑定大核", json);
            Assert.DoesNotContain("\\u6e38", json, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(filePath);
        }
    }
}
