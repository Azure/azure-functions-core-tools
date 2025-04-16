This project contains the test framework that can be used for tests. `FuncCommand` is the base class that implements Command defined in the abstractions project.
Each specific type of command, such as `FuncStartCommand`, can inherit from `FuncCommand` and be used in the tests directly.

Note that some of the classes are based off the [.NET sdk repo](https://github.com/dotnet/sdk).