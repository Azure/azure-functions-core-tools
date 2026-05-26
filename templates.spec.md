The goal here is to create a spec for templates workload here.

Key points:
1. Port "func new templates" functionality from main branch and convert into individaul stack specific workload 
2. The workload kind would be content. It would only include the contents of this json file ("C:\Users\nasoni\Downloads\templates_spec.json").
3. The func new commands logic will reside in the CLI. 
4. I want to seperate the v1 templating logic and v2 templating logic in its own projects and a project reference to the main cli project.
5. The metadata would hydrate the template options (template help). For example httpAuthKind for http trigger.
6. Use the create-workload skills to build workload for template content for each stack

Key Areas
DotNet stack	Templates for isolated worker: csproj, Program.cs, host.json, local.settings.json, .gitignore	
Python stack	func new templates (HTTP trigger, Timer, Queue, etc.)
Node stack	func new templates (HTTP trigger, Timer, Queue, etc.)
Templates	Template discovery contract on workloads (per-stack catalogs)
Templates	func new --template <name> --language <lang> flow + interactive picker; replace NewCommand.ExecuteAsync placeholder
Templates	func templates list (replacement for legacy ListTemplatesAction)
Templates	Create templates package



Okay so this document needs to be seperated in 2 sections
1. templates work load only.
2. What do you need for this workload
    Tempalte and in

Goal
1. Have content only template workload
2. Workload will contain template files required to create / execute the template
3. Templates be an entity independent of bundles. Have a min version relationship with bundles to ensure compatibility

Non-goals
1. 
Definitions

Architecture
- Template specifics
    - Versioning
        Templates should have its own versioning independent of bundles. Add min bundle version info to the nuget package tag.

    - relationship to bundles
        - Versioning dependency

    - Func new interaction is the only consumer. All things under func new app populated via templates workload
    - Minversion interaction / logic
    - Layout files and their explanation
    - template source / static templates + bundles
    - workload layout
    - packaging how its done (specifically related to souce generated assembly)

    Project isolation in CLI - Allows us to create future templating engine packages to be consumed by other clients

- Logging
- Telelmetry
- Compatibility and migration
- Open questions


Things that need to be defined for templates workload
1. Files that are needed
2. Their location



Updated func new design

all interactions
1. Func workload dowload for templates, packages and what not.
    id/ content type and all that
2. Func new expereince
    func new --list
    func new --language and what not.
    How do i get this info, what type do i connect to other information


# Currently working on template workload spec only, document only things related to templates workload
    - CLI side implementation details (likely in cli implementation spec)
    - Consider using CLI abstractions
    - core cli components (limit this spec to template workload only)
    - Templating engine uses project isolation via project reference/

    - Func new list
        - Get language and stack from internal services
        - only list the matching templaes or show some error log
    