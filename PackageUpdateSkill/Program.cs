using System.Text;
using PackageUpdateSkill;

Console.OutputEncoding = Encoding.UTF8;

var options = PipelineOptions.Parse(args);
if (options is null)
    return 1;

var runner = new PipelineRunner(options);
return await runner.RunAsync();
