package main

import (
	"archive/zip"
	"bufio"
	"flag"
	"fmt"
	"io"
	"io/ioutil"
	"os"
	"path"
	"runtime"
	"strings"
)

var (
	fbaseDir   string
	fdir       string
	finputFile string
	foutput    string
	fquiet     bool
)

const pathSeparator = string(os.PathSeparator)

func init() {
	flag.StringVar(&fbaseDir, "base-dir", "", "Base dir for the zip archive")
	flag.StringVar(&fdir, "dir", "", "dir to zip")
	flag.StringVar(&finputFile, "input-file", "", "a file containing a list of files to archive")
	flag.StringVar(&foutput, "output", "", "Output file name")
	flag.BoolVar(&fquiet, "quiet", false, "no output")
}

func validateFlags() {
	if fdir == "" && finputFile == "" && fbaseDir == "" && foutput == "" {
		flag.Usage()
		os.Exit(0)
	}

	if foutput == "" {
		flag.Usage()
		panic("output file name is required")
	}

	if fdir == "" && finputFile == "" {
		flag.Usage()
		panic("file and path can't both be empty")
	}

	if fdir != "" && finputFile != "" {
		flag.Usage()
		panic("file and path are mutually exclusive")
	}

	if fbaseDir == "" && fdir != "" {
		fbaseDir = fdir
	}

	if !strings.HasSuffix(fbaseDir, pathSeparator) {
		fbaseDir = fbaseDir + pathSeparator
	}
}

func zipDir(filename, dir string) error {
	newZipFile, err := os.Create(filename)
	if err != nil {
		return err
	}
	defer newZipFile.Close()

	zipWriter := zip.NewWriter(newZipFile)
	defer zipWriter.Close()

	if err = addFiles(zipWriter, dir, dir); err != nil {
		return err
	}

	return nil
}

func zipFiles(filename string, files []string, baseInZip string) error {
	newZipFile, err := os.Create(filename)
	if err != nil {
		return err
	}
	defer newZipFile.Close()

	zipWriter := zip.NewWriter(newZipFile)
	defer zipWriter.Close()

	for _, file := range files {
		if err = addFileToZip(zipWriter, file, baseInZip); err != nil {
			return err
		}
	}
	return nil
}

func addFileToZip(zipWriter *zip.Writer, filePath, baseInZip string) error {
	fileToZip, err := os.Open(filePath)
	if err != nil {
		return err
	}
	defer fileToZip.Close()

	info, err := fileToZip.Stat()
	if err != nil {
		return err
	}

	header, err := zip.FileInfoHeader(info)
	if err != nil {
		return err
	}

	baseLength := len(baseInZip)
	if !strings.HasSuffix(baseInZip, pathSeparator) {
		baseLength++
	}

	header.Name = filePath[baseLength:]
	if runtime.GOOS == "windows" {
		header.Name = strings.ReplaceAll(header.Name, "\\", "/")
	}

	header.Method = zip.Deflate
	if !fquiet {
		fmt.Printf("%s => %s\n", filePath, header.Name)
	}
	writer, err := zipWriter.CreateHeader(header)
	if err != nil {
		return err
	}
	_, err = io.Copy(writer, fileToZip)
	return err
}

func addFiles(w *zip.Writer, dirPath, baseInZip string) error {
	files, err := ioutil.ReadDir(dirPath)
	if err != nil {
		return err
	}

	for _, file := range files {
		if !file.IsDir() {
			err := addFileToZip(w, path.Join(dirPath, file.Name()), baseInZip)
			if err != nil {
				return err
			}
		} else if file.IsDir() {
			newBase := path.Join(dirPath, file.Name())
			addFiles(w, newBase, baseInZip)
		}
	}
	return nil
}

// https://stackoverflow.com/a/18479916/3234163
func readAllLines(path string) ([]string, error) {
	file, err := os.Open(path)
	if err != nil {
		return nil, err
	}
	defer file.Close()

	var lines []string
	scanner := bufio.NewScanner(file)
	for scanner.Scan() {
		lines = append(lines, scanner.Text())
	}
	return lines, scanner.Err()
}

func main() {
	flag.Parse()
	validateFlags()
	var err error

	if fdir != "" {
		err = zipDir(foutput, fdir)
	} else {
		var lines []string
		lines, err = readAllLines(finputFile)
		if err == nil {
			err = zipFiles(foutput, lines, fbaseDir)
		}
	}

	if err != nil {
		panic(err)
	}
}
