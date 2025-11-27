using System;
using System.Text;

namespace SP.Shared.Resource.CodeGen;

public sealed class CodeWriter(int indentSize = 4)
{
    private readonly StringBuilder _sb = new();
    private readonly string _indentText = new(' ', indentSize);

    public int IndentLevel { get; private set; }

    public void Indent() => IndentLevel++;

    public void Unindent()
    {
        if (IndentLevel > 0)
            IndentLevel--;
    }

    public void WriteLine(string? text = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            _sb.AppendLine();
            return;
        }
        
        for (var i = 0; i < IndentLevel; i++)
            _sb.Append(_indentText);
        
        _sb.AppendLine(text);
    }

    public void WriteRaw(string text)
        => _sb.Append(text);

    public IDisposable Block(string headerLine)
    {
        WriteLine(headerLine);
        WriteLine("{");
        Indent();
        return new BlockScope(this);
    }

    private sealed class BlockScope(CodeWriter writer) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            writer.Unindent();
            writer.WriteLine("}");
        }
    }
    
    public override string ToString() => _sb.ToString();
}
