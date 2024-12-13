FROM mcr.microsoft.com/dotnet/runtime:8.0

RUN apt-get update
RUN apt-get -y install fuse-zip

WORKDIR /tst

COPY ZippedOnWindows.zip .
COPY run-test.sh .

RUN chmod +x run-test.sh
RUN mkdir mnt

ENTRYPOINT ["./run-test.sh"]