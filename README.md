# ContainerFS

[![NuGet Version](https://img.shields.io/nuget/v/ContainerFS.svg?style=flat)](https://www.nuget.org/packages/ContainerFS/) [![NuGet](https://img.shields.io/nuget/dt/ContainerFS.svg)](https://www.nuget.org/packages/ContainerFS) 

Self-contained container file system

For a sample app exercising ContainerFS and a CLI tool, please see the embedded projects.

## Help or feedback

First things first - do you need help or have feedback?  Contact me at joel dot christner at gmail dot com or file an issue here!

## New in v1.1.3

- XML documentation

## Description

ContainerFS is a self-contained single-user filesystem written in C# with support for files and directories.  ContainerFS is available under the MIT license.  ContainerFS is tested and compatible with Mono.

Core use cases for ContainerFS:
- self-contained file system, single large BLOB containing a nested filesystem

## Important notes

ContainerFS is still early in development and there are a large number of performance, scalability, consistency, and efficiency optimizations we wish to make.  While we have high aspirations on performance, please be aware it's not there yet.  Some items on our roadmap:
- multi-user support
- journaling for crash consistency

## Simple example

Refer to the Test project for a thorough example.
```
using ContainerFS;
...
// create a new container
// filename, description, block size, block count, debugging
Container c = new Container(filename, "My Container", 4096, 4096, false);

// or, load an existing container
// filename, debugging
Container c = Container.FromFile(filename, false);

// display container stats
Console.WriteLine(c.ToString());

// enumerate a directory
// use Linux-style paths, i.e. / or /foo/bar
List<string> Files;
List<string> subdirs;
long position;
c.ReadDirectory(path, out files, out subdirs, out position);

// write a file
byte[] writeData = Encoding.UTF8.GetBytes("Hello, world!");
c.WriteFile("/", "helloworld.txt", writeData);

// read a file
byte[] readData = c.ReadFile("/", "helloworld.txt");

// delete a file
c.DeleteFile("/", "helloworld.txt");

// create a directory
c.WriteDirectory("/newdirectory");
c.WriteDirectory("/newdirectory/temp");

// delete a directory
c.DeleteDirectory("/newdirectory/temp");
```

## Exceptions

ContainerFS commonly uses DirectoryNotFoundException and IOException.  Exceptions of type IOException will contain a message such as "Directory not found".

## Using the CLI

The CLI project produces a simple-to-use command line tool for creating or interacting with a container.
```
Create a container:
cfs container.cfs create --params=4096,4096 --debug

Enumerate the door directory:
cfs container.cfs dir --path=/

Create some directories:
cfs container.cfs mkdir --path=/foo
cfs container.cfs mkdir --path=/bar
cfs container.cfs mkdir --path=/foo/bar

Remove a directory (must be empty!):
cfs container.cfs rmdir --path=/foo/bar

Create a file using CLI text:
echo Hello world! | cfs container.cfs write --file=/test.txt
echo I'll delete you later! | cfs container.cfs write --file=/temp.txt

Import a file:
cfs container.cfs write --file=/sample.txt < sample.txt

Read a file:
cfs container.cfs read --file=/test.txt
cfs container.cfs read --file=/temp.txt
cfs container.cfs read --file=/sample.txt

Delete a file:
cfs container.cfs delete --file=/temp.txt
```

## Running under Mono

ContainerFS works well in Mono environments to the extent that we have tested it.  It is recommended that when running under Mono, you execute the containing EXE using --server and after using the Mono Ahead-of-Time Compiler (AOT).
```
mono --aot=nrgctx-trampolines=8096,nimt-trampolines=8096,ntrampolines=4048 --server myapp.exe
mono --server myapp.exe
```

## Version history

Refer to CHANGELOG.md
