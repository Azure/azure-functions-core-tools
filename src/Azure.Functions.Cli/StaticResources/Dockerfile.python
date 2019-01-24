FROM mcr.microsoft.com/azure-functions/python:2.0

COPY . /home/site/wwwroot

RUN cd /home/site/wwwroot && \
    pip install -r requirements.txt