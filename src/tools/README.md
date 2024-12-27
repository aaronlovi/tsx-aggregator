
# TSX Stock Scraper Auxiliary Tools

This repository contains auxiliary tools for the TSX stock scraper server and web server application. These tools help with data migration and other tasks related to managing stock market data.

## Table of Contents

- [Introduction](#introduction)
- [Migrate.js Tool](#migratejs-tool)
- [Getting Started](#getting-started)
  - [Prerequisites](#prerequisites)
  - [Installation](#installation)
- [Usage](#usage)
- [Contributing](#contributing)
- [License](#license)

## Introduction

The TSX Stock Scraper Auxiliary Tools repository hosts various tools that complement the main TSX stock scraper server and web server application. These tools are designed to simplify tasks such as data migration, management, and maintenance.

## Migrate.js Tool

### Overview

The `migrate.js` tool is used for a one-time data migration task. It allows you to copy data from a Postgres database to an Amazon DynamoDB table. This can be useful when transitioning from a local development environment to a cloud-based setup.

### Getting Started

#### Prerequisites

Before using the `migrate.js` tool, make sure you have the following prerequisites installed:

- Node.js: [Download Node.js](https://nodejs.org/)
- npm (Node Package Manager): Included with Node.js installation
- AWS CLI: [Install AWS CLI](https://aws.amazon.com/cli/)

#### Installation

1. Clone this repository to your local machine:

   ```bash
   git clone https://github.com/yourusername/tsx-auxiliary-tools.git
   ```

2. Navigate to the project directory:

   ```bash
   cd tsx-auxiliary-tools
   ```

3. Install the required Node.js packages:

   ```bash
   npm install
   ```

### Usage

To use the `migrate.js` tool for data migration, follow these steps:

1. Configure AWS CLI credentials if you haven't already:

   ```bash
   aws configure
   ```

   Follow the prompts to set up your AWS credentials.

2. Open the `migrate.js` script in a text editor and configure the source Postgres database connection details and the destination DynamoDB table information.

3. Run the migration script:

   ```bash
   npm run migrate
   ```

   This will initiate the data migration process.

4. Monitor the progress and check for any errors in the console output.

5. Once the migration is complete, verify the data in your DynamoDB table using the AWS Management Console or AWS CLI.

## License

This source code is made available for reading and reference purposes only. You may not use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of this software without explicit permission from the author.

**All Rights Reserved**

## Contact

Aaron Lovi - aaronlovi@gmail.com
