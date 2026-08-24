using LibTreeMapView.Core.Model;

namespace LibTreeMapView.Core.Coff;

/// <summary>セクション名と属性から大分類を決める。</summary>
public static class SectionClassifier
{
    /// <summary>'$' 以降を落としたグループ名を返す。<c>.text$mn</c> → <c>.text</c>。</summary>
    public static string GetGroupName(string sectionName)
    {
        int index = sectionName.IndexOf('$');
        return index > 0 ? sectionName[..index] : sectionName;
    }

    public static SectionKind Classify(string sectionName, uint characteristics)
    {
        string group = GetGroupName(sectionName);

        switch (group)
        {
            case ".text":
            case ".textbss":
                return SectionKind.Code;
            case ".data":
                return SectionKind.Data;
            case ".rdata":
            case ".rodata":
            case ".gfids":
            case ".giats":
                return SectionKind.ReadOnlyData;
            case ".bss":
                return SectionKind.UninitializedData;
            case ".debug":
            case ".debug_info":
            case ".debug_abbrev":
            case ".debug_line":
            case ".debug_str":
                return SectionKind.Debug;
            case ".pdata":
            case ".xdata":
            case ".eh_frame":
                return SectionKind.ExceptionHandling;
            case ".drectve":
            case ".chks64":
            case ".llvm_addrsig":
                return SectionKind.Directive;
            case ".idata":
            case ".edata":
            case ".didat":
                return SectionKind.Import;
        }

        if (group.StartsWith(".debug", StringComparison.Ordinal))
        {
            return SectionKind.Debug;
        }

        if ((characteristics & CoffConstants.ScnLnkInfo) != 0)
        {
            return SectionKind.Directive;
        }

        if ((characteristics & CoffConstants.ScnCntCode) != 0)
        {
            return SectionKind.Code;
        }

        if ((characteristics & CoffConstants.ScnCntUninitializedData) != 0)
        {
            return SectionKind.UninitializedData;
        }

        if ((characteristics & CoffConstants.ScnMemDiscardable) != 0)
        {
            return SectionKind.Debug;
        }

        if ((characteristics & CoffConstants.ScnCntInitializedData) != 0)
        {
            return (characteristics & CoffConstants.ScnMemWrite) != 0
                ? SectionKind.Data
                : SectionKind.ReadOnlyData;
        }

        return SectionKind.Other;
    }
}
