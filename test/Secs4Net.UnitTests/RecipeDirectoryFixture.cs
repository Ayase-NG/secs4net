using System;
using System.IO;

namespace Secs4Net.UnitTests;

/// <summary>
/// 隔离的配方目录，避免测试依赖或修改生产默认路径。
/// </summary>
internal sealed class RecipeDirectoryFixture : IDisposable
{
    public string DirectoryPath { get; }

    /// <summary>
    /// 创建唯一临时根目录及指定配方子目录。
    /// </summary>
    public RecipeDirectoryFixture(params string[] recipeIds)
    {
        DirectoryPath = Path.Combine(Path.GetTempPath(), "secs-gem-recipes", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(DirectoryPath);
        // 测试可显式构造一级或嵌套目录，以检查只匹配一级文件夹的规则。
        foreach (var recipeId in recipeIds)
            Directory.CreateDirectory(Path.Combine(DirectoryPath, recipeId));
    }

    /// <summary>
    /// 清理本次测试拥有的临时目录，不触碰生产配方。
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath))
            Directory.Delete(DirectoryPath, recursive: true);
    }
}
